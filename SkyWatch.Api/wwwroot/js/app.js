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

            case 'noflyzone':
                title.textContent = data.name;
                html = `
                    <div class="info-row"><span class="label">Designation</span><span class="value">${data.id}</span></div>
                    <div class="info-row"><span class="label">Type</span><span class="value">${data.type}</span></div>
                    <div class="info-row"><span class="label">Floor</span><span class="value">${data.floor.toLocaleString()} ft</span></div>
                    <div class="info-row"><span class="label">Ceiling</span><span class="value">${data.ceiling >= 99999 ? 'Unlimited' : data.ceiling.toLocaleString() + ' ft'}</span></div>
                    <div class="info-row"><span class="label">Radius</span><span class="value">${data.radiusKm} km</span></div>
                    <div class="info-row"><span class="label">Center</span><span class="value">${data.center[1].toFixed(4)}&deg;, ${data.center[0].toFixed(4)}&deg;</span></div>
                `;
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
        const keyNames = ['CesiumIonToken', 'OpenSkyUsername', 'OpenSkyPassword', 'AisHubApiKey', 'UsgsM2MApiToken', 'UsgsM2MUsername', 'UsgsM2MPassword'];
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

    // FAA Special Use Airspace data (static dataset of well-known US restricted/prohibited areas)
    const STATIC_AIRSPACE = [
        // Prohibited Areas
        { id: 'P-56A', name: 'P-56A — White House', type: 'Prohibited', center: [-77.0365, 38.8977], radiusKm: 1.3, floor: 0, ceiling: 18000, color: '#ef4444' },
        { id: 'P-56B', name: 'P-56B — Naval Observatory', type: 'Prohibited', center: [-77.0700, 38.9177], radiusKm: 1.3, floor: 0, ceiling: 18000, color: '#ef4444' },
        { id: 'P-49', name: 'P-49 — Camp David', type: 'Prohibited', center: [-77.4630, 39.6480], radiusKm: 5.6, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'P-40', name: 'P-40 — Cape Canaveral', type: 'Prohibited', center: [-80.6041, 28.3922], radiusKm: 18, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'P-204', name: 'P-204 — Pantex Nuclear Facility', type: 'Prohibited', center: [-101.9540, 35.3170], radiusKm: 7.4, floor: 0, ceiling: 15000, color: '#ef4444' },
        { id: 'P-205', name: 'P-205 — Amarillo, TX', type: 'Prohibited', center: [-101.8410, 35.1830], radiusKm: 5.6, floor: 0, ceiling: 12500, color: '#ef4444' },
        { id: 'P-67', name: 'P-67 — Bush Compound Kennebunkport', type: 'Prohibited', center: [-70.4490, 43.3520], radiusKm: 1.8, floor: 0, ceiling: 3000, color: '#ef4444' },

        // Major Restricted Areas
        { id: 'R-2508', name: 'R-2508 — Edwards AFB / China Lake', type: 'Restricted', center: [-117.6000, 35.7000], radiusKm: 80, floor: 0, ceiling: 99999, color: '#f97316' },
        { id: 'R-2301', name: 'R-2301 — White Sands Missile Range', type: 'Restricted', center: [-106.3500, 32.9500], radiusKm: 60, floor: 0, ceiling: 99999, color: '#f97316' },
        { id: 'R-4808N', name: 'R-4808N — Nellis / Groom Lake (Area 51)', type: 'Restricted', center: [-115.8000, 37.2350], radiusKm: 40, floor: 0, ceiling: 99999, color: '#f97316' },
        { id: 'R-4809', name: 'R-4809 — Nevada Test Site', type: 'Restricted', center: [-116.0500, 36.8500], radiusKm: 35, floor: 0, ceiling: 99999, color: '#f97316' },
        { id: 'R-5107', name: 'R-5107 — Fort Bragg', type: 'Restricted', center: [-79.0000, 35.1400], radiusKm: 15, floor: 0, ceiling: 20000, color: '#f97316' },
        { id: 'R-2206', name: 'R-2206 — Fort Hood', type: 'Restricted', center: [-97.7700, 31.1400], radiusKm: 25, floor: 0, ceiling: 17999, color: '#f97316' },
        { id: 'R-5601', name: 'R-5601 — Cherry Point MCAS', type: 'Restricted', center: [-76.9300, 34.9100], radiusKm: 18, floor: 0, ceiling: 50000, color: '#f97316' },
        { id: 'R-6602', name: 'R-6602 — Eglin AFB', type: 'Restricted', center: [-86.5300, 30.4700], radiusKm: 45, floor: 0, ceiling: 99999, color: '#f97316' },
        { id: 'R-3004', name: 'R-3004 — Aberdeen Proving Ground', type: 'Restricted', center: [-76.1500, 39.4700], radiusKm: 12, floor: 0, ceiling: 25000, color: '#f97316' },
        { id: 'R-4401', name: 'R-4401 — Camp Atterbury', type: 'Restricted', center: [-86.0200, 39.3500], radiusKm: 10, floor: 0, ceiling: 13000, color: '#f97316' },

        // Military Operations Areas (MOAs)
        { id: 'MOA-Tombstone', name: 'Tombstone MOA — AZ', type: 'MOA', center: [-110.2000, 31.7000], radiusKm: 50, floor: 200, ceiling: 18000, color: '#f59e0b' },
        { id: 'MOA-Condor', name: 'Condor MOA — CA', type: 'MOA', center: [-118.3000, 35.4000], radiusKm: 45, floor: 200, ceiling: 18000, color: '#f59e0b' },
        { id: 'MOA-Juniper', name: 'Juniper MOA — OR', type: 'MOA', center: [-118.5000, 43.5000], radiusKm: 55, floor: 100, ceiling: 18000, color: '#f59e0b' },
        { id: 'MOA-Bronco', name: 'Bronco MOA — TX', type: 'MOA', center: [-100.5000, 30.2000], radiusKm: 60, floor: 100, ceiling: 18000, color: '#f59e0b' },
        { id: 'MOA-Hays', name: 'Hays MOA — KS', type: 'MOA', center: [-99.3000, 38.8000], radiusKm: 50, floor: 500, ceiling: 18000, color: '#f59e0b' },
        { id: 'MOA-Whiskey', name: 'Whiskey MOA — NC', type: 'MOA', center: [-77.5000, 35.5000], radiusKm: 40, floor: 500, ceiling: 18000, color: '#f59e0b' },

        // International Notable Zones
        { id: 'NFZ-DMZ-KR', name: 'Korean DMZ', type: 'Prohibited', center: [127.5000, 38.0000], radiusKm: 100, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'NFZ-Chernobyl', name: 'Chernobyl Exclusion Zone', type: 'Prohibited', center: [30.0987, 51.3890], radiusKm: 30, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'NFZ-Dimona', name: 'Dimona Nuclear Facility — Israel', type: 'Restricted', center: [35.1470, 31.0020], radiusKm: 25, floor: 0, ceiling: 99999, color: '#f97316' },
        { id: 'NFZ-Mecca', name: 'Mecca Haram — Saudi Arabia', type: 'Prohibited', center: [39.8262, 21.4225], radiusKm: 20, floor: 0, ceiling: 99999, color: '#ef4444' },
        { id: 'NFZ-Buckingham', name: 'Buckingham Palace TRA — London', type: 'Restricted', center: [-0.1419, 51.5014], radiusKm: 2.5, floor: 0, ceiling: 2500, color: '#f97316' },
    ];

    function renderNoFlyZones() {
        nfzEntities.entities.removeAll();
        if (!nfzState.enabled) return;

        const filter = nfzState.filter;
        const zones = filter === 'all'
            ? STATIC_AIRSPACE
            : STATIC_AIRSPACE.filter(z => z.type === filter || (filter === 'TFR' && z.type === 'TFR'));

        for (const zone of zones) {
            const color = Cesium.Color.fromCssColorString(zone.color);

            // Ground circle
            nfzEntities.entities.add({
                id: 'nfz-' + zone.id,
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

            // 3D cylinder wall (shows vertical extent)
            const floorM = zone.floor * 0.3048; // feet to meters
            const ceilM = Math.min(zone.ceiling, 60000) * 0.3048;

            nfzEntities.entities.add({
                id: 'nfz-wall-' + zone.id,
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
    const API_STATUS_SOURCES = ['Celestrak', 'OpenSky', 'AISHub', 'Copernicus', 'USGS'];

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

    console.log('Observable Smarts initialized.');

})();
