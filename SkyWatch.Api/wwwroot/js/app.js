// SkyWatch — OSINT Live Globe
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
        satellites: { enabled: true, filter: 'all', data: [] },
        swaths: { enabled: true, data: [] },
        imagery: { enabled: false, data: [] },
        flights: { enabled: false, filter: 'all', data: [] },
        ships: { enabled: false, filter: 'all', data: [] },
        isLive: true,
        roiMode: false,
        roiPoints: []
    };

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
                    scaleByDistance: new Cesium.NearFarScalar(1e6, 1.5, 1e8, 0.5),
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
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
                    outlineWidth: 2,
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
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
                    scaleByDistance: new Cesium.NearFarScalar(1e6, 1, 5e7, 0.3),
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
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

        for (let i = 0; i < data.length; i++) {
            const scene = data[i];
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
                    scaleByDistance: new Cesium.NearFarScalar(1e5, 2, 1e7, 0.5),
                    disableDepthTestDistance: Number.POSITIVE_INFINITY
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
                if (data.sensor) {
                    html += `<div class="info-row"><span class="label">Sensor</span><span class="value">${data.sensor}</span></div>`;
                }
                if (data.swathWidthKm) {
                    html += `<div class="info-row"><span class="label">Swath</span><span class="value">${data.swathWidthKm} km</span></div>`;
                }
                html += `<button class="btn" style="margin-top: 8px; width: 100%;" onclick="window.skywatch.showTrack(${data.noradId})">Show Ground Track</button>`;
                break;

            case 'flight':
                title.textContent = data.callsign || data.icao24 || 'Unknown Flight';
                html = `
                    <div class="info-row"><span class="label">ICAO24</span><span class="value">${data.icao24}</span></div>
                    <div class="info-row"><span class="label">Callsign</span><span class="value">${data.callsign || '—'}</span></div>
                    <div class="info-row"><span class="label">Category</span><span class="value">${data.category || 'Unknown'}</span></div>
                    <div class="info-row"><span class="label">Latitude</span><span class="value">${(data.latitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Longitude</span><span class="value">${(data.longitude || 0).toFixed(4)}&deg;</span></div>
                    <div class="info-row"><span class="label">Altitude</span><span class="value">${((data.altitudeM || 0) * 3.281).toFixed(0)} ft (${((data.altitudeM || 0) / 1000).toFixed(1)} km)</span></div>
                    <div class="info-row"><span class="label">Speed</span><span class="value">${((data.velocityMs || 0) * 1.944).toFixed(0)} kts</span></div>
                    <div class="info-row"><span class="label">Heading</span><span class="value">${(data.heading || 0).toFixed(0)}&deg;</span></div>
                    <div class="info-row"><span class="label">On Ground</span><span class="value">${data.onGround ? 'Yes' : 'No'}</span></div>
                `;
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

    // ===== Layer Toggles =====
    document.getElementById('toggle-satellites').addEventListener('change', function () {
        state.satellites.enabled = this.checked;
        satelliteEntities.show = this.checked;
        if (this.checked) refreshSatellites();
    });

    document.getElementById('toggle-swaths').addEventListener('change', function () {
        state.swaths.enabled = this.checked;
        swathEntities.show = this.checked;
        if (this.checked) refreshSwaths();
    });

    document.getElementById('toggle-imagery').addEventListener('change', function () {
        state.imagery.enabled = this.checked;
        imageryEntities.show = this.checked;
        if (this.checked) refreshImagery();
    });

    document.getElementById('toggle-flights').addEventListener('change', function () {
        state.flights.enabled = this.checked;
        flightEntities.show = this.checked;
        if (this.checked) refreshFlights();
    });

    document.getElementById('toggle-ships').addEventListener('change', function () {
        state.ships.enabled = this.checked;
        shipEntities.show = this.checked;
        if (this.checked) refreshShips();
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

    // ===== Auto-refresh Loops =====
    function startRefreshLoops() {
        // Initial loads
        refreshSatellites();
        refreshSwaths();

        // Periodic refresh
        setInterval(refreshSatellites, REFRESH_INTERVALS.satellites);
        setInterval(refreshSwaths, REFRESH_INTERVALS.swaths);
        setInterval(refreshFlights, REFRESH_INTERVALS.flights);
        setInterval(refreshShips, REFRESH_INTERVALS.ships);
        setInterval(refreshImagery, REFRESH_INTERVALS.imagery);
    }

    startRefreshLoops();
    updateTimeDisplay();

    console.log('SkyWatch initialized.');

})();
