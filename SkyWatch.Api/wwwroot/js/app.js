// Observable Smarts — OSINT Live Globe
// Main application JavaScript

(function () {
    'use strict';

    // ===== Configuration =====
    const API_BASE = '';
    const REFRESH_INTERVALS = {
        satellites: 10000,   // 10s (positions recomputed server-side)
        flights: 15000,      // 15s
        ships: 60000,        // 60s
        imagery: 300000,     // 5 min
        swaths: 10000        // 10s (moves with satellite)
    };

    // ===== Cesium Setup =====
    Cesium.Ion.defaultAccessToken = undefined; // Will use OSM by default

    const viewer = new Cesium.Viewer('cesiumContainer', {
        imageryProvider: new Cesium.OpenStreetMapImageryProvider({
            url: 'https://tile.openstreetmap.org/'
        }),
        baseLayerPicker: false,
        geocoder: false,
        homeButton: false,
        sceneModePicker: false,
        selectionIndicator: false,
        navigationHelpButton: false,
        animation: false,
        timeline: false,
        fullscreenButton: false,
        infoBox: false,
        creditContainer: document.createElement('div'), // Hide credits
        requestRenderMode: false,
        scene3DOnly: true,
        skyAtmosphere: new Cesium.SkyAtmosphere(),
        contextOptions: {
            webgl: { alpha: false }
        }
    });

    // Dark globe styling
    viewer.scene.globe.enableLighting = false;
    viewer.scene.backgroundColor = Cesium.Color.fromCssColorString('#0a0e17');
    viewer.scene.globe.baseColor = Cesium.Color.fromCssColorString('#0d1117');

    // Default camera position
    viewer.camera.setView({
        destination: Cesium.Cartesian3.fromDegrees(0, 20, 20000000)
    });

    // ===== Data Sources =====
    const satelliteEntities = new Cesium.CustomDataSource('satellites');
    const swathEntities = new Cesium.CustomDataSource('swaths');
    const imageryEntities = new Cesium.CustomDataSource('imagery');
    const flightEntities = new Cesium.CustomDataSource('flights');
    const shipEntities = new Cesium.CustomDataSource('ships');
    const roiEntities = new Cesium.CustomDataSource('roi');

    viewer.dataSources.add(satelliteEntities);
    viewer.dataSources.add(swathEntities);
    viewer.dataSources.add(imageryEntities);
    viewer.dataSources.add(flightEntities);
    viewer.dataSources.add(shipEntities);
    viewer.dataSources.add(roiEntities);

    // ===== State =====
    const state = {
        satellites: { enabled: false, filter: 'all', data: [] },
        swaths: { enabled: false, data: [] },
        imagery: { enabled: false, sourceFilter: 'all', sensorFilter: 'all', data: [] },
        flights: { enabled: false, filter: 'all', data: [] },
        ships: { enabled: false, filter: 'all', data: [] },
        isLive: true,
        roiMode: false,
        roiPoints: []
    };

    // ===== Source → Sensor Mapping =====
    const SOURCE_SENSORS = {
        all: ['Sentinel-2 MSI', 'Sentinel-1 SAR', 'Sentinel-3', 'Sentinel-5P', 'Landsat', 'MODIS Terra'],
        Copernicus: ['Sentinel-2 MSI', 'Sentinel-1 SAR', 'Sentinel-3', 'Sentinel-5P'],
        USGS: ['Landsat'],
        'NASA CMR': ['MODIS Terra']
    };

    function buildSensorOptions(source) {
        const container = document.getElementById('imagery-sensor-options');
        container.innerHTML = '';
        const sensors = SOURCE_SENSORS[source] || SOURCE_SENSORS.all;

        // "All" option
        const allLabel = document.createElement('label');
        const allRadio = document.createElement('input');
        allRadio.type = 'radio';
        allRadio.name = 'imagery-sensor';
        allRadio.value = 'all';
        allRadio.checked = true;
        allLabel.appendChild(allRadio);
        allLabel.appendChild(document.createTextNode(' All Sensors'));
        container.appendChild(allLabel);

        for (const sensor of sensors) {
            const label = document.createElement('label');
            const radio = document.createElement('input');
            radio.type = 'radio';
            radio.name = 'imagery-sensor';
            radio.value = sensor;
            label.appendChild(radio);
            label.appendChild(document.createTextNode(' ' + sensor));
            container.appendChild(label);
        }

        // Reset sensor filter and wire up change listeners
        state.imagery.sensorFilter = 'all';
        container.querySelectorAll('input[name="imagery-sensor"]').forEach(radio => {
            radio.addEventListener('change', function () {
                state.imagery.sensorFilter = this.value;
                if (state.imagery.data.length > 0) renderImagery(state.imagery.data);
            });
        });

        // Re-render with new filter
        if (state.imagery.data.length > 0) renderImagery(state.imagery.data);
    }

    // ===== Category Colors =====
    const SAT_COLORS = {
        EarthObservation: Cesium.Color.fromCssColorString('#10b981'),
        ISS: Cesium.Color.fromCssColorString('#ef4444'),
        Starlink: Cesium.Color.fromCssColorString('#64748b'),
        GPS: Cesium.Color.fromCssColorString('#f59e0b'),
        Debris: Cesium.Color.fromCssColorString('#475569'),
        Weather: Cesium.Color.fromCssColorString('#06b6d4'),
        Communications: Cesium.Color.fromCssColorString('#8b5cf6'),
        Military: Cesium.Color.fromCssColorString('#ef4444'),
        Unknown: Cesium.Color.fromCssColorString('#334155')
    };

    const FLIGHT_COLORS = {
        Commercial: Cesium.Color.fromCssColorString('#f59e0b'),
        Cargo: Cesium.Color.fromCssColorString('#f97316'),
        Military: Cesium.Color.fromCssColorString('#ef4444'),
        GeneralAviation: Cesium.Color.fromCssColorString('#a3e635'),
        Unknown: Cesium.Color.fromCssColorString('#fbbf24')
    };

    const SHIP_COLORS = {
        Cargo: Cesium.Color.fromCssColorString('#3b82f6'),
        Tanker: Cesium.Color.fromCssColorString('#8b5cf6'),
        Passenger: Cesium.Color.fromCssColorString('#10b981'),
        Fishing: Cesium.Color.fromCssColorString('#06b6d4'),
        MilitaryGovernment: Cesium.Color.fromCssColorString('#ef4444'),
        Pleasure: Cesium.Color.fromCssColorString('#f59e0b'),
        Tug: Cesium.Color.fromCssColorString('#64748b'),
        HighSpeedCraft: Cesium.Color.fromCssColorString('#ec4899'),
        Unknown: Cesium.Color.fromCssColorString('#475569')
    };

    // ===== API Helpers =====
    async function fetchJson(url) {
        try {
            const resp = await fetch(API_BASE + url);
            if (!resp.ok) return null;
            return await resp.json();
        } catch (e) {
            console.warn('Fetch failed:', url, e);
            return null;
        }
    }

    async function postJson(url, body) {
        try {
            const resp = await fetch(API_BASE + url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(body)
            });
            if (!resp.ok) return null;
            return await resp.json();
        } catch (e) {
            console.warn('Post failed:', url, e);
            return null;
        }
    }

    // ===== Satellite Layer =====
    async function refreshSatellites() {
        if (!state.satellites.enabled) return;

        const data = await fetchJson('/api/satellites');
        if (!data) return;

        state.satellites.data = data;
        updateSatCount(data.length);
        renderSatellites(data);
    }

    function renderSatellites(data) {
        satelliteEntities.entities.removeAll();

        const filter = state.satellites.filter;
        const filtered = filter === 'all' ? data : data.filter(s => s.category === filter);

        // For large datasets (Starlink), show a subset to maintain performance
        const maxDisplay = 2000;
        const display = filtered.length > maxDisplay
            ? filtered.slice(0, maxDisplay)
            : filtered;

        for (const sat of display) {
            const color = SAT_COLORS[sat.category] || SAT_COLORS.Unknown;
            const size = sat.category === 'ISS' ? 12 :
                         sat.category === 'EarthObservation' ? 6 :
                         sat.category === 'Debris' ? 2 : 4;

            satelliteEntities.entities.add({
                id: 'sat-' + sat.noradId,
                position: Cesium.Cartesian3.fromDegrees(sat.longitude, sat.latitude, sat.altitudeKm * 1000),
                point: {
                    pixelSize: size,
                    color: color,
                    outlineColor: Cesium.Color.WHITE.withAlpha(0.3),
                    outlineWidth: 1,
                    scaleByDistance: new Cesium.NearFarScalar(1e6, 1.5, 1e8, 0.5)
                },
                properties: {
                    type: 'satellite',
                    data: sat
                }
            });
        }
    }

    function updateSatCount(count) {
        document.getElementById('sat-count').textContent = `Satellites: ${count.toLocaleString()}`;
        const dot = document.getElementById('sat-status');
        dot.className = 'status-dot' + (count > 0 ? '' : ' offline');
    }

    // ===== Swath Layer =====
    async function refreshSwaths() {
        if (!state.swaths.enabled) return;

        const data = await fetchJson('/api/satellites/imaging');
        if (!data) return;

        state.swaths.data = data;
        renderSwaths(data);
    }

    function renderSwaths(data) {
        swathEntities.entities.removeAll();

        for (const sat of data) {
            // Satellite marker (larger, distinct)
            swathEntities.entities.add({
                id: 'imaging-' + sat.noradId,
                position: Cesium.Cartesian3.fromDegrees(sat.longitude, sat.latitude, sat.altitudeKm * 1000),
                point: {
                    pixelSize: 10,
                    color: Cesium.Color.fromCssColorString('#10b981'),
                    outlineColor: Cesium.Color.WHITE,
                    outlineWidth: 2
                },
                label: {
                    text: sat.name,
                    font: '11px sans-serif',
                    fillColor: Cesium.Color.WHITE,
                    outlineColor: Cesium.Color.BLACK,
                    outlineWidth: 2,
                    style: Cesium.LabelStyle.FILL_AND_OUTLINE,
                    verticalOrigin: Cesium.VerticalOrigin.BOTTOM,
                    pixelOffset: new Cesium.Cartesian2(0, -14),
                    scaleByDistance: new Cesium.NearFarScalar(1e6, 1, 5e7, 0.3)
                },
                properties: {
                    type: 'imaging-satellite',
                    data: sat
                }
            });

            // Swath footprint polygon
            if (sat.swathFootprint && sat.swathFootprint.coordinates && sat.swathFootprint.coordinates.length > 0) {
                const ring = sat.swathFootprint.coordinates[0];
                const positions = [];
                for (const coord of ring) {
                    positions.push(coord[0], coord[1]);
                }

                swathEntities.entities.add({
                    id: 'swath-' + sat.noradId,
                    polygon: {
                        hierarchy: Cesium.Cartesian3.fromDegreesArray(positions),
                        material: Cesium.Color.fromCssColorString('#10b981').withAlpha(0.15),
                        outline: true,
                        outlineColor: Cesium.Color.fromCssColorString('#10b981').withAlpha(0.6),
                        outlineWidth: 1,
                        height: 0,
                        classificationType: Cesium.ClassificationType.BOTH
                    }
                });
            }
        }
    }

    // ===== Imagery Layer =====
    async function refreshImagery() {
        if (!state.imagery.enabled) return;

        const data = await fetchJson('/api/imagery/recent');
        if (!data) return;

        state.imagery.data = data;
        renderImagery(data);
    }

    function renderImagery(data) {
        imageryEntities.entities.removeAll();

        // Apply source filter
        let filtered = data;
        if (state.imagery.sourceFilter !== 'all') {
            filtered = filtered.filter(s => s.source === state.imagery.sourceFilter);
        }
        // Apply sensor filter
        if (state.imagery.sensorFilter !== 'all') {
            filtered = filtered.filter(s => s.sensor && s.sensor.indexOf(state.imagery.sensorFilter) !== -1);
        }

        for (let i = 0; i < filtered.length; i++) {
            const scene = filtered[i];
            if (!scene.footprint || !scene.footprint.coordinates || scene.footprint.coordinates.length === 0) continue;

            const ring = scene.footprint.coordinates[0];
            const positions = [];
            for (const coord of ring) {
                positions.push(coord[0], coord[1]);
            }

            // Color based on recency
            const hoursAgo = (Date.now() - new Date(scene.acquisitionDate).getTime()) / 3600000;
            const alpha = Math.max(0.1, 0.4 - hoursAgo * 0.01);
            const color = hoursAgo < 6
                ? Cesium.Color.fromCssColorString('#8b5cf6')
                : Cesium.Color.fromCssColorString('#6366f1');

            imageryEntities.entities.add({
                id: 'imagery-' + scene.id,
                polygon: {
                    hierarchy: Cesium.Cartesian3.fromDegreesArray(positions),
                    material: color.withAlpha(alpha),
                    outline: true,
                    outlineColor: color.withAlpha(0.7),
                    outlineWidth: 1,
                    height: 0,
                    classificationType: Cesium.ClassificationType.BOTH
                },
                properties: {
                    type: 'imagery',
                    data: scene
                }
            });
        }
    }

    // ===== Flight Layer =====
    async function refreshFlights() {
        if (!state.flights.enabled) return;

        const data = await fetchJson('/api/flights');
        if (!data) return;

        state.flights.data = data;
        updateFlightCount(data.length);
        renderFlights(data);
    }

    function renderFlights(data) {
        flightEntities.entities.removeAll();

        const filter = state.flights.filter;
        const filtered = filter === 'all' ? data : data.filter(f => f.category === filter);

        // Limit display for performance
        const maxDisplay = 5000;
        const display = filtered.length > maxDisplay ? filtered.slice(0, maxDisplay) : filtered;

        for (const flight of display) {
            if (flight.latitude == null || flight.longitude == null) continue;

            const color = FLIGHT_COLORS[flight.category] || FLIGHT_COLORS.Unknown;
            const alt = flight.altitudeM || 0;

            flightEntities.entities.add({
                id: 'flight-' + flight.icao24,
                position: Cesium.Cartesian3.fromDegrees(flight.longitude, flight.latitude, alt),
                point: {
                    pixelSize: 4,
                    color: color,
                    outlineColor: Cesium.Color.WHITE.withAlpha(0.2),
                    outlineWidth: 1,
                    scaleByDistance: new Cesium.NearFarScalar(1e5, 2, 1e7, 0.5)
                },
                properties: {
                    type: 'flight',
                    data: flight
                }
            });

            // Trail
            if (flight.trail && flight.trail.length > 1) {
                const trailPositions = flight.trail.map(t =>
                    Cesium.Cartesian3.fromDegrees(t.longitude, t.latitude, t.altitudeM || 0)
                );
                flightEntities.entities.add({
                    id: 'flight-trail-' + flight.icao24,
                    polyline: {
                        positions: trailPositions,
                        width: 1,
                        material: color.withAlpha(0.4),
                        clampToGround: false
                    }
                });
            }
        }
    }

    function updateFlightCount(count) {
        document.getElementById('flight-count').textContent = `Flights: ${count.toLocaleString()}`;
        const dot = document.getElementById('flight-status');
        dot.className = 'status-dot' + (count > 0 ? '' : ' offline');
    }

    // ===== Ship Layer =====
    async function refreshShips() {
        if (!state.ships.enabled) return;

        const data = await fetchJson('/api/ships');
        if (!data) return;

        state.ships.data = data;
        updateShipCount(data.length);
        renderShips(data);
    }

    function renderShips(data) {
        shipEntities.entities.removeAll();

        const filter = state.ships.filter;
        const filtered = filter === 'all' ? data : data.filter(s => s.vesselType === filter);

        for (const ship of filtered) {
            if (ship.latitude == null || ship.longitude == null) continue;

            const color = SHIP_COLORS[ship.vesselType] || SHIP_COLORS.Unknown;

            shipEntities.entities.add({
                id: 'ship-' + ship.mmsi,
                position: Cesium.Cartesian3.fromDegrees(ship.longitude, ship.latitude, 0),
                point: {
                    pixelSize: 5,
                    color: color,
                    outlineColor: Cesium.Color.WHITE.withAlpha(0.3),
                    outlineWidth: 1,
                    scaleByDistance: new Cesium.NearFarScalar(1e4, 2, 1e7, 0.3),
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
                },
                properties: {
                    type: 'ship',
                    data: ship
                }
            });

            // Trail
            if (ship.trail && ship.trail.length > 1) {
                const trailPositions = ship.trail.map(t =>
                    Cesium.Cartesian3.fromDegrees(t.longitude, t.latitude, 0)
                );
                shipEntities.entities.add({
                    id: 'ship-trail-' + ship.mmsi,
                    polyline: {
                        positions: trailPositions,
                        width: 1,
                        material: color.withAlpha(0.4),
                        clampToGround: true
                    }
                });
            }
        }
    }

    function updateShipCount(count) {
        document.getElementById('ship-count').textContent = `Ships: ${count.toLocaleString()}`;
        const dot = document.getElementById('ship-status');
        dot.className = 'status-dot' + (count > 0 ? '' : ' offline');
    }

    // ===== Entity Click Handler =====
    const handler = new Cesium.ScreenSpaceEventHandler(viewer.scene.canvas);

    handler.setInputAction(function (click) {
        if (state.roiMode) {
            handleRoiClick(click);
            return;
        }

        const picked = viewer.scene.pick(click.position);
        if (!Cesium.defined(picked) || !picked.id || !picked.id.properties) {
            hidePopup();
            return;
        }

        const props = picked.id.properties;
        const type = props.type ? props.type.getValue() : null;
        const data = props.data ? props.data.getValue() : null;

        if (!type || !data) {
            hidePopup();
            return;
        }

        showPopup(type, data);
    }, Cesium.ScreenSpaceEventType.LEFT_CLICK);

    // ===== Popup =====
    function showPopup(type, data) {
        const popup = document.getElementById('info-popup');
        const title = document.getElementById('popup-title');
        const body = document.getElementById('popup-body');

        let html = '';

        switch (type) {
            case 'satellite':
            case 'imaging-satellite':
                title.textContent = data.name || 'Unknown Satellite';
                html = `
                    <div class="info-row"><span class="label">NORAD ID</span><span class="value">${data.noradId}</span></div>
                    <div class="info-row"><span class="label">Category</span><span class="value">${data.category || 'Unknown'}</span></div>
                    <div class="info-row"><span class="label">Latitude</span><span class="value">${(data.latitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Longitude</span><span class="value">${(data.longitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Altitude</span><span class="value">${(data.altitudeKm || 0).toFixed(1)} km</span></div>
                    <div class="info-row"><span class="label">Velocity</span><span class="value">${(data.velocityKmS || 0).toFixed(2)} km/s</span></div>
                `;
                if (data.intlDesignator) {
                    html += `<div class="info-row"><span class="label">Intl Designator</span><span class="value">${data.intlDesignator}</span></div>`;
                }
                if (data.inclinationDeg != null) {
                    html += `<div class="info-row"><span class="label">Inclination</span><span class="value">${data.inclinationDeg}&deg;</span></div>`;
                }
                if (data.periodMinutes != null) {
                    html += `<div class="info-row"><span class="label">Orbital Period</span><span class="value">${data.periodMinutes.toFixed(1)} min</span></div>`;
                }
                if (data.apogeeKm != null && data.perigeeKm != null) {
                    html += `<div class="info-row"><span class="label">Apogee</span><span class="value">${data.apogeeKm.toFixed(1)} km</span></div>`;
                    html += `<div class="info-row"><span class="label">Perigee</span><span class="value">${data.perigeeKm.toFixed(1)} km</span></div>`;
                }
                if (data.eccentricityValue != null) {
                    html += `<div class="info-row"><span class="label">Eccentricity</span><span class="value">${data.eccentricityValue}</span></div>`;
                }
                if (data.epochAge) {
                    html += `<div class="info-row"><span class="label">TLE Age</span><span class="value">${data.epochAge}</span></div>`;
                }
                if (data.sensor) {
                    html += `<div class="info-row"><span class="label">Sensor</span><span class="value">${data.sensor}</span></div>`;
                }
                if (data.swathWidthKm) {
                    html += `<div class="info-row"><span class="label">Swath</span><span class="value">${data.swathWidthKm} km</span></div>`;
                }
                html += `<button class="btn" style="margin-top: 8px; width: 100%;" onclick="window.skywatch.showTrack(${data.noradId})">Show Ground Track</button>`;
                break;

            case 'flight':
                const EMITTER_CATEGORIES = {
                    0: 'No info', 1: 'No ADS-B emitter info', 2: 'Light (< 15,500 lbs)',
                    3: 'Small (15,500\u201375,000 lbs)', 4: 'Large (75,000\u2013300,000 lbs)',
                    5: 'High Vortex Large', 6: 'Heavy (> 300,000 lbs)',
                    7: 'High Performance (> 5g)', 8: 'Rotorcraft',
                    9: 'Glider / Sailplane', 10: 'Lighter-than-air',
                    11: 'Parachutist / Skydiver', 12: 'Ultralight / Hang-glider',
                    14: 'UAV / Drone', 15: 'Space Vehicle',
                    17: 'Surface Emergency Vehicle', 18: 'Surface Service Vehicle'
                };
                const acType = data.emitterCategory != null ? (EMITTER_CATEGORIES[data.emitterCategory] || `Category ${data.emitterCategory}`) : '—';

                const vrMs = data.verticalRate;
                let vrDisplay = '—';
                if (vrMs != null) {
                    const vrFpm = (vrMs * 196.85).toFixed(0);
                    vrDisplay = vrMs > 0 ? `+${vrFpm} ft/min` : `${vrFpm} ft/min`;
                }

                let squawkDisplay = data.squawk || '—';
                if (data.squawk === '7500') squawkDisplay = '7500 (HIJACK)';
                else if (data.squawk === '7600') squawkDisplay = '7600 (RADIO FAIL)';
                else if (data.squawk === '7700') squawkDisplay = '7700 (EMERGENCY)';

                title.textContent = data.callsign || data.icao24 || 'Unknown Flight';
                html = `
                    <div class="info-row"><span class="label">ICAO24</span><span class="value">${data.icao24}</span></div>
                    <div class="info-row"><span class="label">Callsign</span><span class="value">${data.callsign || '—'}</span></div>
                    <div class="info-row"><span class="label">Aircraft Type</span><span class="value">${acType}</span></div>
                    <div class="info-row"><span class="label">Category</span><span class="value">${data.category || 'Unknown'}</span></div>
                    <div class="info-row"><span class="label">Origin</span><span class="value">${data.originCountry || '—'}</span></div>
                    <div class="info-row"><span class="label">Latitude</span><span class="value">${(data.latitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Longitude</span><span class="value">${(data.longitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Altitude</span><span class="value">${((data.altitudeM || 0) * 3.281).toFixed(0)} ft (${((data.altitudeM || 0) / 1000).toFixed(1)} km)</span></div>
                    <div class="info-row"><span class="label">Speed</span><span class="value">${((data.velocityMs || 0) * 1.944).toFixed(0)} kts</span></div>
                    <div class="info-row"><span class="label">Heading</span><span class="value">${(data.heading || 0).toFixed(0)}&deg;</span></div>
                    <div class="info-row"><span class="label">Vertical Rate</span><span class="value">${vrDisplay}</span></div>
                    <div class="info-row"><span class="label">Squawk</span><span class="value">${squawkDisplay}</span></div>
                    <div class="info-row"><span class="label">On Ground</span><span class="value">${data.onGround ? 'Yes' : 'No'}</span></div>
                `;
                html += `<button class="btn btn-lookup" style="margin-top: 8px; width: 100%;" onclick="window.skywatch.lookupAircraft('${data.icao24}')">Lookup Aircraft Details</button>`;
                break;

            case 'ship':
                title.textContent = data.name || data.mmsi || 'Unknown Vessel';
                html = `
                    <div class="info-row"><span class="label">MMSI</span><span class="value">${data.mmsi}</span></div>
                    <div class="info-row"><span class="label">Name</span><span class="value">${data.name || '—'}</span></div>
                    <div class="info-row"><span class="label">Type</span><span class="value">${data.vesselType || 'Unknown'}</span></div>
                    <div class="info-row"><span class="label">Flag</span><span class="value">${data.flag || '—'}</span></div>
                    <div class="info-row"><span class="label">Latitude</span><span class="value">${(data.latitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Longitude</span><span class="value">${(data.longitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Speed</span><span class="value">${(data.speedKnots || 0).toFixed(1)} kts</span></div>
                    <div class="info-row"><span class="label">Heading</span><span class="value">${(data.heading || 0).toFixed(0)}&deg;</span></div>
                    <div class="info-row"><span class="label">Destination</span><span class="value">${data.destination || '—'}</span></div>
                `;
                break;

            case 'imagery':
                title.textContent = data.sensor || 'Satellite Imagery';
                html = `
                    <div class="info-row"><span class="label">Sensor</span><span class="value">${data.sensor}</span></div>
                    <div class="info-row"><span class="label">Source</span><span class="value">${data.source}</span></div>
                    <div class="info-row"><span class="label">Acquired</span><span class="value">${new Date(data.acquisitionDate).toLocaleString()}</span></div>
                `;
                if (data.cloudCoverPercent != null) {
                    html += `<div class="info-row"><span class="label">Cloud Cover</span><span class="value">${data.cloudCoverPercent.toFixed(1)}%</span></div>`;
                }
                if (data.thumbnailUrl) {
                    html += `<img class="info-thumbnail" src="${data.thumbnailUrl}" alt="Preview" onerror="this.style.display='none'">`;
                }
                if (data.fullImageUrl) {
                    html += `<a class="info-link" href="${data.fullImageUrl}" target="_blank" rel="noopener">View Full Image &rarr;</a>`;
                }
                break;

            case 'noflyzone':
                title.textContent = data.name;
                html = `
                    <div class="info-row"><span class="label">Designation</span><span class="value">${data.id}</span></div>
                    <div class="info-row"><span class="label">Type</span><span class="value">${data.type}</span></div>
                    <div class="info-row"><span class="label">Floor</span><span class="value">${data.floor.toLocaleString()} ft</span></div>
                    <div class="info-row"><span class="label">Ceiling</span><span class="value">${data.ceiling >= 99999 ? 'Unlimited' : data.ceiling.toLocaleString() + ' ft'}</span></div>
                `;
                if (data.radiusKm && data.radiusKm !== '—') {
                    html += `<div class="info-row"><span class="label">Radius</span><span class="value">${data.radiusKm} km</span></div>`;
                }
                html += `<div class="info-row"><span class="label">Center</span><span class="value">${data.center[1].toFixed(4)}&deg;, ${data.center[0].toFixed(4)}&deg;</span></div>`;

                // Show all additional FAA properties
                if (data.details && typeof data.details === 'object') {
                    const FAA_LABELS = {
                        'IDENT': 'Identifier', 'LOCAL_TYPE': 'Local Type',
                        'LOWER_UOM': 'Floor Units', 'UPPER_UOM': 'Ceiling Units',
                        'EFFECTIVE_DATE': 'Effective', 'EXPIRATION_DATE': 'Expires',
                        'SCHEDULE': 'Schedule', 'CITY': 'City', 'STATE': 'State',
                        'COUNTRY': 'Country', 'AGENCY': 'Agency',
                        'CONTROLLING_AGENCY': 'Controlling Agency',
                        'SECTOR': 'Sector', 'REASON': 'Reason',
                        'NOTAM_ID': 'NOTAM', 'OBJECTID': null,
                        'NAME': null, 'TYPE_CODE': null,
                        'LOWER_VAL': null, 'UPPER_VAL': null,
                        'lower_val': null, 'upper_val': null,
                        'name': null, 'ident': null, 'type_code': null
                    };
                    for (const [key, val] of Object.entries(data.details)) {
                        if (FAA_LABELS[key] === null) continue; // already shown above
                        const label = FAA_LABELS[key] || key.replace(/_/g, ' ').replace(/\b\w/g, c => c.toUpperCase());
                        html += `<div class="info-row"><span class="label">${label}</span><span class="value">${val}</span></div>`;
                    }
                }
                break;

            default:
                return;
        }

        body.innerHTML = html;
        popup.classList.add('visible');
    }

    function hidePopup() {
        document.getElementById('info-popup').classList.remove('visible');
    }

    document.getElementById('popup-close').addEventListener('click', hidePopup);

    // ===== Ground Track =====
    window.skywatch = window.skywatch || {};
    window.skywatch.showTrack = async function (noradId) {
        const trackData = await fetchJson(`/api/satellites/${noradId}/track?minutes=90`);
        if (!trackData || trackData.length === 0) return;

        // Remove old track
        const existing = satelliteEntities.entities.getById('track-' + noradId);
        if (existing) satelliteEntities.entities.remove(existing);

        const positions = trackData.map(p =>
            Cesium.Cartesian3.fromDegrees(p.longitude, p.latitude, 0)
        );

        satelliteEntities.entities.add({
            id: 'track-' + noradId,
            polyline: {
                positions: positions,
                width: 2,
                material: new Cesium.PolylineDashMaterialProperty({
                    color: Cesium.Color.CYAN.withAlpha(0.7),
                    dashLength: 8
                }),
                clampToGround: true
            }
        });
    };

    window.skywatch.lookupAircraft = async function (icao24) {
        const body = document.getElementById('popup-body');
        const existingBtn = body.querySelector('.btn-lookup');
        if (existingBtn) existingBtn.textContent = 'Looking up...';

        const meta = await fetchJson(`/api/flights/${icao24}/metadata`);
        if (!meta) {
            if (existingBtn) existingBtn.textContent = 'No data found';
            return;
        }

        let metaHtml = '<div style="border-top: 1px solid var(--border); margin-top: 8px; padding-top: 8px;">';
        metaHtml += '<div class="info-row"><span class="label" style="font-weight: bold;">Aircraft Details</span><span class="value"></span></div>';
        if (meta.manufacturer) metaHtml += `<div class="info-row"><span class="label">Manufacturer</span><span class="value">${meta.manufacturer}</span></div>`;
        if (meta.model) metaHtml += `<div class="info-row"><span class="label">Model</span><span class="value">${meta.model}</span></div>`;
        if (meta.typeCode) metaHtml += `<div class="info-row"><span class="label">Type Code</span><span class="value">${meta.typeCode}</span></div>`;
        if (meta.registration) metaHtml += `<div class="info-row"><span class="label">Registration</span><span class="value">${meta.registration}</span></div>`;
        if (meta.operator) metaHtml += `<div class="info-row"><span class="label">Operator</span><span class="value">${meta.operator}</span></div>`;
        if (meta.owner) metaHtml += `<div class="info-row"><span class="label">Owner</span><span class="value">${meta.owner}</span></div>`;
        metaHtml += '</div>';

        if (existingBtn) existingBtn.remove();
        body.insertAdjacentHTML('beforeend', metaHtml);
    };

    // ===== Region of Interest =====
    function handleRoiClick(click) {
        const cartesian = viewer.camera.pickEllipsoid(click.position, viewer.scene.globe.ellipsoid);
        if (!cartesian) return;

        const carto = Cesium.Cartographic.fromCartesian(cartesian);
        const lon = Cesium.Math.toDegrees(carto.longitude);
        const lat = Cesium.Math.toDegrees(carto.latitude);

        state.roiPoints.push([lon, lat]);

        // Add marker
        roiEntities.entities.add({
            position: Cesium.Cartesian3.fromDegrees(lon, lat),
            point: {
                pixelSize: 8,
                color: Cesium.Color.YELLOW,
                outlineColor: Cesium.Color.WHITE,
                outlineWidth: 2,
                disableDepthTestDistance: Number.POSITIVE_INFINITY
            }
        });

        // Draw polygon if we have 3+ points
        if (state.roiPoints.length >= 3) {
            const existing = roiEntities.entities.getById('roi-polygon');
            if (existing) roiEntities.entities.remove(existing);

            const positions = state.roiPoints.flatMap(p => [p[0], p[1]]);
            roiEntities.entities.add({
                id: 'roi-polygon',
                polygon: {
                    hierarchy: Cesium.Cartesian3.fromDegreesArray(positions),
                    material: Cesium.Color.YELLOW.withAlpha(0.15),
                    outline: true,
                    outlineColor: Cesium.Color.YELLOW,
                    outlineWidth: 2,
                    height: 0
                }
            });
        }
    }

    document.getElementById('btn-roi').addEventListener('click', function () {
        state.roiMode = true;
        state.roiPoints = [];
        roiEntities.entities.removeAll();
        document.getElementById('roi-panel').classList.add('visible');
        document.getElementById('roi-results').innerHTML = '<p style="color: var(--text-muted); font-size: 12px;">Click on the globe to draw a polygon...</p>';
    });

    document.getElementById('btn-roi-close').addEventListener('click', function () {
        state.roiMode = false;
        state.roiPoints = [];
        roiEntities.entities.removeAll();
        document.getElementById('roi-panel').classList.remove('visible');
    });

    document.getElementById('btn-roi-clear').addEventListener('click', function () {
        state.roiPoints = [];
        roiEntities.entities.removeAll();
        document.getElementById('roi-results').innerHTML = '<p style="color: var(--text-muted); font-size: 12px;">Click on the globe to draw a polygon...</p>';
    });

    document.getElementById('btn-roi-predict').addEventListener('click', async function () {
        if (state.roiPoints.length < 3) {
            document.getElementById('roi-results').innerHTML = '<p style="color: var(--accent-orange); font-size: 12px;">Draw at least 3 points on the globe first.</p>';
            return;
        }

        const ring = [...state.roiPoints, state.roiPoints[0]]; // Close the ring
        const polygon = {
            type: 'Polygon',
            coordinates: [ring]
        };

        document.getElementById('roi-results').innerHTML = '<p style="color: var(--text-muted); font-size: 12px;">Computing passes...</p>';

        const predictions = await postJson('/api/regionofinterest/predict', {
            polygon: polygon,
            hoursAhead: 24
        });

        if (!predictions || predictions.length === 0) {
            document.getElementById('roi-results').innerHTML = '<p style="color: var(--text-muted); font-size: 12px;">No passes predicted in the next 24 hours.</p>';
            return;
        }

        let html = '';
        for (const pass of predictions) {
            const time = new Date(pass.passTime).toLocaleString(undefined, {
                month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit'
            });
            html += `<div class="roi-result-item">
                <span class="sat-name">${pass.name}</span> (${pass.sensor})<br>
                <span class="pass-time">${time} — Swath: ${pass.swathWidthKm} km</span>
            </div>`;
        }
        document.getElementById('roi-results').innerHTML = html;
    });

    // ===== On-Demand Layer Toggles =====
    // Track interval IDs so we can stop polling when toggled off
    const refreshIntervals = {};

    async function enableStream(stream) {
        try { await fetch(`/api/workers/${stream}/enable`, { method: 'POST' }); } catch (e) { console.warn('Failed to enable worker:', e); }
    }
    async function disableStream(stream) {
        try { await fetch(`/api/workers/${stream}/disable`, { method: 'POST' }); } catch (e) { console.warn('Failed to disable worker:', e); }
    }

    document.getElementById('toggle-satellites').addEventListener('change', function () {
        state.satellites.enabled = this.checked;
        satelliteEntities.show = this.checked;
        if (this.checked) {
            enableStream('satellites');
            refreshSatellites();
            refreshIntervals.satellites = setInterval(refreshSatellites, REFRESH_INTERVALS.satellites);
        } else {
            disableStream('satellites');
            clearInterval(refreshIntervals.satellites);
            satelliteEntities.entities.removeAll();
        }
    });

    document.getElementById('toggle-swaths').addEventListener('change', function () {
        state.swaths.enabled = this.checked;
        swathEntities.show = this.checked;
        if (this.checked) {
            refreshSwaths();
            refreshIntervals.swaths = setInterval(refreshSwaths, REFRESH_INTERVALS.swaths);
        } else {
            clearInterval(refreshIntervals.swaths);
            swathEntities.entities.removeAll();
        }
    });

    document.getElementById('toggle-imagery').addEventListener('change', function () {
        state.imagery.enabled = this.checked;
        imageryEntities.show = this.checked;
        if (this.checked) {
            enableStream('imagery');
            refreshImagery();
            refreshIntervals.imagery = setInterval(refreshImagery, REFRESH_INTERVALS.imagery);
        } else {
            disableStream('imagery');
            clearInterval(refreshIntervals.imagery);
            imageryEntities.entities.removeAll();
        }
    });

    document.getElementById('toggle-flights').addEventListener('change', function () {
        state.flights.enabled = this.checked;
        flightEntities.show = this.checked;
        if (this.checked) {
            enableStream('flights');
            refreshFlights();
            refreshIntervals.flights = setInterval(refreshFlights, REFRESH_INTERVALS.flights);
        } else {
            disableStream('flights');
            clearInterval(refreshIntervals.flights);
            flightEntities.entities.removeAll();
        }
    });

    document.getElementById('toggle-ships').addEventListener('change', function () {
        state.ships.enabled = this.checked;
        shipEntities.show = this.checked;
        if (this.checked) {
            enableStream('ships');
            refreshShips();
            refreshIntervals.ships = setInterval(refreshShips, REFRESH_INTERVALS.ships);
        } else {
            disableStream('ships');
            clearInterval(refreshIntervals.ships);
            shipEntities.entities.removeAll();
        }
    });

    // Satellite category filter
    document.querySelectorAll('input[name="sat-filter"]').forEach(radio => {
        radio.addEventListener('change', function () {
            state.satellites.filter = this.value;
            if (state.satellites.data.length > 0) renderSatellites(state.satellites.data);
        });
    });

    // Flight category filter
    document.querySelectorAll('input[name="flight-filter"]').forEach(radio => {
        radio.addEventListener('change', function () {
            state.flights.filter = this.value;
            if (state.flights.data.length > 0) renderFlights(state.flights.data);
        });
    });

    // Ship type filter
    document.querySelectorAll('input[name="ship-filter"]').forEach(radio => {
        radio.addEventListener('change', function () {
            state.ships.filter = this.value;
            if (state.ships.data.length > 0) renderShips(state.ships.data);
        });
    });

    // Imagery source filter — rebuild sensor options when source changes
    document.querySelectorAll('input[name="imagery-source"]').forEach(radio => {
        radio.addEventListener('change', function () {
            state.imagery.sourceFilter = this.value;
            buildSensorOptions(this.value);
        });
    });

    // Build initial sensor options for "all" sources
    buildSensorOptions('all');

    // ===== Per-Source Imagery Refresh =====
    function setupSourceRefresh(btnId, source) {
        document.getElementById(btnId).addEventListener('click', async function () {
            const statusEl = document.getElementById('imagery-refresh-status');
            const originalText = this.textContent;
            this.disabled = true;
            this.textContent = '...';
            statusEl.textContent = '';

            try {
                const resp = await fetch(`/api/imagery/refresh/${source}`, { method: 'POST' });
                const result = await resp.json();
                statusEl.textContent = `${result.source}: ${result.sceneCount} scenes (total: ${result.totalScenes})`;
                if (state.imagery.enabled) {
                    await refreshImagery();
                }
            } catch (err) {
                statusEl.textContent = `${source} refresh failed: ${err.message}`;
            }

            this.disabled = false;
            this.textContent = originalText;
        });
    }
    setupSourceRefresh('btn-refresh-copernicus', 'copernicus');
    setupSourceRefresh('btn-refresh-usgs', 'usgs');
    setupSourceRefresh('btn-refresh-nasa', 'nasa');

    // ===== Sidebar Toggle (Mobile) =====
    document.getElementById('sidebar-toggle').addEventListener('click', function () {
        const sidebar = document.getElementById('sidebar');
        sidebar.classList.toggle('collapsed');
        sidebar.classList.toggle('open');
    });

    // ===== Base Layer Picker =====
    document.getElementById('base-layer-select').addEventListener('change', function () {
        const layers = viewer.imageryLayers;

        // Remove all imagery layers
        while (layers.length > 0) {
            layers.remove(layers.get(0));
        }

        switch (this.value) {
            case 'osm':
                layers.addImageryProvider(new Cesium.OpenStreetMapImageryProvider({
                    url: 'https://tile.openstreetmap.org/'
                }));
                break;
            case 'dark':
                // Use a dark tile style
                layers.addImageryProvider(new Cesium.UrlTemplateImageryProvider({
                    url: 'https://cartodb-basemaps-{s}.global.ssl.fastly.net/dark_all/{z}/{x}/{y}.png',
                    subdomains: 'abcd',
                    credit: 'CartoDB Dark Matter'
                }));
                break;
            case 'bing':
                // Requires Cesium Ion token
                layers.addImageryProvider(new Cesium.OpenStreetMapImageryProvider({
                    url: 'https://tile.openstreetmap.org/'
                }));
                break;
        }
    });

    // Set initial dark base layer
    (function setInitialBaseLayer() {
        const layers = viewer.imageryLayers;
        while (layers.length > 0) {
            layers.remove(layers.get(0));
        }
        layers.addImageryProvider(new Cesium.UrlTemplateImageryProvider({
            url: 'https://cartodb-basemaps-{s}.global.ssl.fastly.net/dark_all/{z}/{x}/{y}.png',
            subdomains: 'abcd',
            credit: 'CartoDB Dark Matter'
        }));
    })();

    // ===== Data Capture Toggle =====
    const captureToggle = document.getElementById('toggle-capture');
    const captureStatus = document.getElementById('capture-status');
    const captureDownload = document.getElementById('btn-capture-download');
    const captureClear = document.getElementById('btn-capture-clear');

    captureToggle.addEventListener('change', async function () {
        const enabled = this.checked;
        await fetch('/api/capture/toggle', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ enabled })
        });
        captureDownload.style.display = enabled ? 'block' : 'none';
        captureClear.style.display = enabled ? 'block' : 'none';
        captureStatus.style.display = enabled ? 'block' : 'none';
        if (enabled) {
            captureStatus.textContent = 'Recording data streams...';
            refreshCaptureStatus();
        } else {
            captureStatus.textContent = 'Capture stopped.';
        }
    });

    async function refreshCaptureStatus() {
        if (!captureToggle.checked) return;
        try {
            const status = await fetchJson('/api/capture/status');
            if (status && status.files) {
                const entries = Object.entries(status.files);
                if (entries.length > 0) {
                    const parts = entries.map(([name, info]) => {
                        const sizeKb = (info.sizeBytes / 1024).toFixed(1);
                        return `${name}: ${sizeKb} KB`;
                    });
                    captureStatus.textContent = parts.join(' | ');
                }
            }
        } catch { /* ignore */ }
    }

    // Refresh capture file sizes periodically
    setInterval(refreshCaptureStatus, 15000);

    captureDownload.addEventListener('click', async function () {
        const status = await fetchJson('/api/capture/status');
        if (!status || !status.files) return;
        for (const streamName of Object.keys(status.files)) {
            const a = document.createElement('a');
            a.href = `/api/capture/download/${streamName}`;
            a.download = `${streamName}.jsonl`;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        }
    });

    captureClear.addEventListener('click', async function () {
        if (!confirm('Clear all captured log files?')) return;
        await fetch('/api/capture/clear', { method: 'DELETE' });
        captureStatus.textContent = 'Logs cleared.';
        refreshCaptureStatus();
    });

    // ===== Time Controls =====
    const timeScrubber = document.getElementById('time-scrubber');
    const timeDisplay = document.getElementById('time-display');
    const liveBadge = document.getElementById('live-badge');

    function updateTimeDisplay() {
        const now = new Date();
        if (state.isLive) {
            timeDisplay.textContent = now.toUTCString().replace('GMT', 'UTC');
        } else {
            const minutesAgo = 1440 - parseInt(timeScrubber.value);
            const pastTime = new Date(now.getTime() - minutesAgo * 60000);
            timeDisplay.textContent = pastTime.toUTCString().replace('GMT', 'UTC');
        }
    }

    timeScrubber.addEventListener('input', function () {
        if (parseInt(this.value) === 1440) {
            state.isLive = true;
            liveBadge.classList.add('active');
        } else {
            state.isLive = false;
            liveBadge.classList.remove('active');
        }
        updateTimeDisplay();
    });

    liveBadge.addEventListener('click', function () {
        state.isLive = true;
        timeScrubber.value = 1440;
        liveBadge.classList.add('active');
        updateTimeDisplay();
    });

    // Update time display every second
    setInterval(updateTimeDisplay, 1000);

    // ===== Settings Panel =====
    document.getElementById('btn-settings').addEventListener('click', async function () {
        document.getElementById('settings-overlay').classList.add('visible');

        // Fetch key status
        try {
            const resp = await fetch(API_BASE + '/api/config/status');
            if (resp.ok) {
                const status = await resp.json();
                for (const [key, value] of Object.entries(status)) {
                    if (key === 'usgsTokenExpired') continue;
                    const dot = document.getElementById('status-' + key);
                    if (dot) {
                        dot.className = 'key-status ' + (value ? 'configured' : 'not-set');
                        dot.textContent = value ? 'Configured' : 'Not set';
                        dot.title = value ? 'This key is saved' : 'No key saved yet';
                    }
                    const input = document.getElementById('key-' + key);
                    if (input) {
                        input.placeholder = value ? '••••••• (saved — leave blank to keep)' : input.placeholder;
                    }
                }

                // Show USGS token expiry warning
                const tokenDot = document.getElementById('status-UsgsM2MApiToken');
                if (status.usgsTokenExpired && tokenDot) {
                    tokenDot.className = 'key-status expired';
                    tokenDot.textContent = 'Expired';
                    tokenDot.title = 'Token was rejected by USGS — please replace it';
                }
            }
        } catch (e) {
            console.warn('Could not fetch key status:', e);
        }
    });

    document.getElementById('settings-close').addEventListener('click', closeSettings);
    document.getElementById('btn-cancel-keys').addEventListener('click', closeSettings);
    document.getElementById('settings-overlay').addEventListener('click', function (e) {
        if (e.target === this) closeSettings();
    });

    function closeSettings() {
        document.getElementById('settings-overlay').classList.remove('visible');
        document.getElementById('settings-message').textContent = '';
        // Clear inputs
        document.querySelectorAll('#settings-modal input').forEach(i => i.value = '');
    }

    document.getElementById('btn-save-keys').addEventListener('click', async function () {
        const keys = {};
        const keyNames = ['CesiumIonToken', 'OpenSkyClientId', 'OpenSkyClientSecret', 'AisHubApiKey', 'UsgsM2MApiToken', 'UsgsM2MUsername', 'UsgsM2MPassword'];
        for (const name of keyNames) {
            const val = document.getElementById('key-' + name).value.trim();
            if (val) keys[name] = val;
        }

        if (Object.keys(keys).length === 0) {
            document.getElementById('settings-message').innerHTML = '<span style="color: var(--accent-orange);">Enter at least one key to save.</span>';
            return;
        }

        try {
            const resp = await fetch(API_BASE + '/api/config/keys', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(keys)
            });
            const result = await resp.json();
            document.getElementById('settings-message').innerHTML = '<span style="color: var(--accent-green);">' + result.message + '</span>';
        } catch (e) {
            document.getElementById('settings-message').innerHTML = '<span style="color: var(--accent-red);">Failed to save keys.</span>';
        }
    });

    // ===== No-Fly Zone Layer =====
    const nfzEntities = new Cesium.CustomDataSource('noflyzone');
    viewer.dataSources.add(nfzEntities);

    const nfzState = { enabled: false, filter: 'all', data: [] };

    // International notable zones (rendered as ellipses — no FAA polygon data for these)
    const STATIC_INTERNATIONAL_ZONES = [
        { id: 'NFZ-DMZ-KR', name: 'Korean DMZ', type: 'Prohibited', center: [127.5000, 38.0000], radiusKm: 100, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'NFZ-Chernobyl', name: 'Chernobyl Exclusion Zone', type: 'Prohibited', center: [30.0987, 51.3890], radiusKm: 30, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'NFZ-Dimona', name: 'Dimona Nuclear Facility — Israel', type: 'Restricted', center: [35.1470, 31.0020], radiusKm: 25, floor: 0, ceiling: 99999, color: '#f97316' },
        { id: 'NFZ-Mecca', name: 'Mecca Haram — Saudi Arabia', type: 'Prohibited', center: [39.8262, 21.4225], radiusKm: 20, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'NFZ-Buckingham', name: 'Buckingham Palace TRA — London', type: 'Restricted', center: [-0.1419, 51.5014], radiusKm: 2.5, floor: 0, ceiling: 2500, color: '#f97316' },
    ];

    // Parse FAA GeoJSON feature into a normalized zone object
    function parseFaaFeature(feature) {
        const p = feature.properties || {};
        // FAA fields: NAME (e.g. "P-56A WASHINGTON, DC"), TYPE_CODE ("P"/"R"), UPPER_VAL, LOWER_VAL
        const name = p.NAME || p.IDENT || p.name || p.ident || 'Unknown';
        const typeCode = p.TYPE_CODE || p.type_code || '';
        let type = 'Restricted';
        let color = '#f97316';
        if (typeCode === 'P' || name.startsWith('P-')) {
            type = 'Prohibited';
            color = '#ef4444';
        } else if (typeCode === 'R' || name.startsWith('R-')) {
            type = 'Restricted';
            color = '#f97316';
        } else if (typeCode === 'MOA' || name.indexOf('MOA') !== -1) {
            type = 'MOA';
            color = '#f59e0b';
        }

        // Altitude — FAA uses various field names
        const floor = p.LOWER_VAL || p.lower_val || p.FLOOR || 0;
        const ceiling = p.UPPER_VAL || p.upper_val || p.CEILING || 99999;

        // Pass through all FAA properties for detailed popup
        const details = {};
        for (const [key, val] of Object.entries(p)) {
            if (val != null && val !== '' && key !== 'SHAPE' && key !== 'Shape') {
                details[key] = val;
            }
        }

        return { name, type, color, floor: Number(floor), ceiling: Number(ceiling), geometry: feature.geometry, details };
    }

    async function renderNoFlyZones() {
        nfzEntities.entities.removeAll();
        if (!nfzState.enabled) return;

        const filter = nfzState.filter;

        // 1. Fetch real FAA polygon data
        const geojson = await fetchJson('/api/airspace/sua');
        if (geojson && geojson.features && geojson.features.length > 0) {
            let faaIdx = 0;
            for (const feature of geojson.features) {
                const zone = parseFaaFeature(feature);

                // Apply filter
                if (filter !== 'all' && zone.type !== filter) continue;
                if (!zone.geometry || !zone.geometry.coordinates) continue;

                const cesiumColor = Cesium.Color.fromCssColorString(zone.color);
                const zoneId = 'faa-' + faaIdx++;

                // Handle both Polygon and MultiPolygon
                const polygons = zone.geometry.type === 'MultiPolygon'
                    ? zone.geometry.coordinates
                    : [zone.geometry.coordinates];

                for (let pi = 0; pi < polygons.length; pi++) {
                    const ring = polygons[pi][0]; // outer ring
                    if (!ring || ring.length < 3) continue;

                    const positions = [];
                    for (const coord of ring) {
                        positions.push(coord[0], coord[1]);
                    }

                    const entityId = polygons.length > 1 ? zoneId + '-' + pi : zoneId;

                    // Ground polygon
                    nfzEntities.entities.add({
                        id: 'nfz-' + entityId,
                        polygon: {
                            hierarchy: Cesium.Cartesian3.fromDegreesArray(positions),
                            material: cesiumColor.withAlpha(0.12),
                            outline: true,
                            outlineColor: cesiumColor.withAlpha(0.7),
                            outlineWidth: 2,
                            height: 0,
                            classificationType: Cesium.ClassificationType.BOTH
                        },
                        label: pi === 0 ? {
                            text: zone.name,
                            font: '11px sans-serif',
                            fillColor: cesiumColor,
                            outlineColor: Cesium.Color.BLACK,
                            outlineWidth: 2,
                            style: Cesium.LabelStyle.FILL_AND_OUTLINE,
                            verticalOrigin: Cesium.VerticalOrigin.CENTER,
                            scaleByDistance: new Cesium.NearFarScalar(1e5, 1, 5e7, 0),
                            disableDepthTestDistance: Number.POSITIVE_INFINITY
                        } : undefined,
                        position: pi === 0 ? Cesium.Cartesian3.fromDegrees(ring[0][0], ring[0][1]) : undefined,
                        properties: {
                            type: 'noflyzone',
                            data: { id: zone.name, name: zone.name, type: zone.type, floor: zone.floor, ceiling: zone.ceiling, center: [ring[0][0], ring[0][1]], radiusKm: '—', details: zone.details || {} }
                        }
                    });

                    // 3D vertical extent
                    const floorM = zone.floor * 0.3048;
                    const ceilM = Math.min(zone.ceiling, 60000) * 0.3048;
                    if (ceilM > floorM) {
                        nfzEntities.entities.add({
                            id: 'nfz-wall-' + entityId,
                            polygon: {
                                hierarchy: Cesium.Cartesian3.fromDegreesArray(positions),
                                material: cesiumColor.withAlpha(0.06),
                                outline: true,
                                outlineColor: cesiumColor.withAlpha(0.3),
                                height: floorM,
                                extrudedHeight: ceilM
                            }
                        });
                    }
                }
            }
        }

        // 2. Render international zones as ellipses (no polygon data available)
        const intlZones = filter === 'all'
            ? STATIC_INTERNATIONAL_ZONES
            : STATIC_INTERNATIONAL_ZONES.filter(z => z.type === filter);

        for (const zone of intlZones) {
            const color = Cesium.Color.fromCssColorString(zone.color);

            nfzEntities.entities.add({
                id: 'nfz-intl-' + zone.id,
                position: Cesium.Cartesian3.fromDegrees(zone.center[0], zone.center[1]),
                ellipse: {
                    semiMajorAxis: zone.radiusKm * 1000,
                    semiMinorAxis: zone.radiusKm * 1000,
                    material: color.withAlpha(0.12),
                    outline: true,
                    outlineColor: color.withAlpha(0.7),
                    outlineWidth: 2,
                    height: 0,
                    classificationType: Cesium.ClassificationType.BOTH
                },
                label: {
                    text: zone.id,
                    font: '11px sans-serif',
                    fillColor: color,
                    outlineColor: Cesium.Color.BLACK,
                    outlineWidth: 2,
                    style: Cesium.LabelStyle.FILL_AND_OUTLINE,
                    verticalOrigin: Cesium.VerticalOrigin.CENTER,
                    scaleByDistance: new Cesium.NearFarScalar(1e5, 1, 5e7, 0),
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
                },
                properties: {
                    type: 'noflyzone',
                    data: zone
                }
            });

            const floorM = zone.floor * 0.3048;
            const ceilM = Math.min(zone.ceiling, 60000) * 0.3048;
            nfzEntities.entities.add({
                id: 'nfz-intl-wall-' + zone.id,
                position: Cesium.Cartesian3.fromDegrees(zone.center[0], zone.center[1]),
                ellipse: {
                    semiMajorAxis: zone.radiusKm * 1000,
                    semiMinorAxis: zone.radiusKm * 1000,
                    material: color.withAlpha(0.06),
                    outline: true,
                    outlineColor: color.withAlpha(0.3),
                    height: floorM,
                    extrudedHeight: ceilM
                }
            });
        }
    }

    // No-fly zone toggle
    document.getElementById('toggle-noflyzone').addEventListener('change', function () {
        nfzState.enabled = this.checked;
        nfzEntities.show = this.checked;
        if (this.checked) renderNoFlyZones();
        else nfzEntities.entities.removeAll();
    });

    // No-fly zone filter
    document.querySelectorAll('input[name="nfz-filter"]').forEach(radio => {
        radio.addEventListener('change', function () {
            nfzState.filter = this.value;
            renderNoFlyZones();
        });
    });

    // ===== API Status Ticker =====
    const API_STATUS_SOURCES = ['Celestrak', 'OpenSky', 'AISHub', 'Copernicus', 'USGS', 'NASA CMR'];

    function formatAgo(isoString) {
        if (!isoString) return 'never';
        const diff = Math.floor((Date.now() - new Date(isoString).getTime()) / 1000);
        if (diff < 0) return 'just now';
        if (diff < 60) return diff + 's ago';
        if (diff < 3600) return Math.floor(diff / 60) + 'm ago';
        if (diff < 86400) return Math.floor(diff / 3600) + 'h ago';
        return Math.floor(diff / 86400) + 'd ago';
    }

    async function refreshApiStatus() {
        try {
            const data = await fetchJson('/api/status');
            if (!data) return;

            const container = document.getElementById('api-status-items');
            if (!container) return;

            container.innerHTML = '';
            for (const name of API_STATUS_SOURCES) {
                const s = data[name];
                const item = document.createElement('div');
                item.className = 'api-status-item';

                let dotClass, detail;
                if (!s || (!s.lastSuccess && !s.lastAttempt)) {
                    dotClass = 'waiting';
                    detail = 'waiting';
                } else if (s.lastError) {
                    dotClass = 'error';
                    const errShort = s.lastHttpStatus ? ('HTTP ' + s.lastHttpStatus) : 'error';
                    detail = errShort + ' · ' + formatAgo(s.lastAttempt);
                } else {
                    dotClass = 'ok';
                    detail = s.lastItemCount.toLocaleString() + ' items · ' + formatAgo(s.lastSuccess);
                }

                item.innerHTML =
                    '<span class="api-dot ' + dotClass + '"></span>' +
                    '<span class="api-name">' + name + '</span>' +
                    '<span class="api-detail">' + detail + '</span>';

                if (s && s.lastError) {
                    item.title = s.lastError;
                }
                container.appendChild(item);
            }
        } catch (e) {
            console.warn('Failed to fetch API status:', e);
        }
    }

    // Toggle ticker visibility
    const ticker = document.getElementById('api-status-ticker');
    const tickerToggle = document.getElementById('api-status-toggle');
    if (tickerToggle) {
        tickerToggle.addEventListener('click', function () {
            ticker.classList.toggle('collapsed');
        });
    }

    // Refresh status every 5 seconds
    setInterval(refreshApiStatus, 5000);
    refreshApiStatus();

    // ===== On-Demand Startup =====
    // Only start streams that are toggled on by default (satellites + swaths)
    function startEnabledStreams() {
        if (state.satellites.enabled) {
            enableStream('satellites');
            refreshSatellites();
            refreshIntervals.satellites = setInterval(refreshSatellites, REFRESH_INTERVALS.satellites);
        }
        if (state.swaths.enabled) {
            refreshSwaths();
            refreshIntervals.swaths = setInterval(refreshSwaths, REFRESH_INTERVALS.swaths);
        }
        // flights, ships, imagery start disabled — user toggles them on
    }

    startEnabledStreams();
    updateTimeDisplay();

    console.log('Observable Smarts initialized.');

})();
