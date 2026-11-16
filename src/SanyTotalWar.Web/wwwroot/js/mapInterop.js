(function () {
    let map = null;
    let dotnet = null;
    let trackLine = null;
    let drawing = false;
    let panDrag = false;
    let prevPan = null;
    let lastVersion = -1;

    const gTerr = () => L.layerGroup().addTo(map);
    let gTerritory, gWars, gPoints, gWalk, gTracks;

    const BLD_GLYPHS = { 'c': ['В', '#e8b23d'], 'f': ['К', '#d8483b'], 'a': ['А', '#6aa3b8'], 'b': ['Б', '#7ba05b'] };

    function hud(text) {
        const el = document.getElementById('walk-hud');
        if (!el) return;
        if (!text) { el.style.display = 'none'; return; }
        el.style.display = 'block';
        el.textContent = text;
    }

    function ringsToLatLngs(rings, holes) {
        const out = [];
        for (const r of rings) out.push(r.map(p => [p[0], p[1]]));
        for (const h of (holes || [])) out.push(h.map(p => [p[0], p[1]]));
        return out;
    }

    function render(state) {
        if (!map) return;
        if (state.version === lastVersion) return;
        lastVersion = state.version;

        gTerritory.clearLayers();
        gWars.clearLayers();
        gPoints.clearLayers();
        gTracks.clearLayers();

        for (const t of state.territories) {
            for (const part of (t.parts || [])) {
                if (!part.length || !part[0].length) continue;
                const latlngs = part.map(r => r.map(p => [p[0], p[1]]));
                const poly = L.polygon(latlngs, {
                    color: t.color,
                    weight: 2,
                    fillColor: t.color,
                    fillOpacity: 0.30,
                });
                poly.on('click', () => dotnet.invokeMethodAsync('OnTerritoryClicked', t.id));
                poly.bindTooltip(t.name, { sticky: true, direction: 'top', className: 'war-tip' });
                gTerritory.addLayer(poly);
            }

            if (t.buildings && t.buildings.length && t.parts && t.parts.length) {
                const ring = t.parts[0][0];
                let la = 0, ln = 0;
                for (const p of ring) { la += p[0]; ln += p[1]; }
                la /= ring.length; ln /= ring.length;
                const icons = t.buildings.map(c => {
                    const g = BLD_GLYPHS[c] || ['?', '#999'];
                    return `<span class="bld" style="border-color:${g[1]};color:${g[1]}" title="">${g[0]}</span>`;
                }).join('');
                const m = L.marker([la, ln], {
                    icon: L.divIcon({
                        className: 'bld-wrap',
                        html: `<div class="bld-icons">${icons}</div>`,
                        iconSize: [64, 18],
                        iconAnchor: [32, 9]
                    }),
                    interactive: false
                });
                gTerritory.addLayer(m);
            }
        }

        for (const w of state.wars) {
            const latlngs = ringsToLatLngs(w.rings, []);
            const glow = L.polygon(latlngs, {
                stroke: false,
                fillColor: '#ff4d3a',
                fillOpacity: 0.16,
                className: 'war-glow'
            });
            const zone = L.polygon(latlngs, {
                color: '#ff5040',
                weight: 2.5,
                dashArray: '9 7',
                fillOpacity: 0.08,
                className: 'war-zone'
            });
            [glow, zone].forEach(l => l.on('click', () => { }));
            gWars.addLayer(glow);
            gWars.addLayer(zone);

            for (const p of (w.points || [])) {
                const cls = p.by ? 'ctrl-point owned' : 'ctrl-point free';
                const style = p.by ? `border-color:${p.color};background:${p.color}33` : '';
                const m = L.marker([p.lat, p.lng], {
                    icon: L.divIcon({
                        className: 'ctrl-wrap',
                        html: `<div class="${cls}" style="${style}"></div>`,
                        iconSize: [18, 18],
                        iconAnchor: [9, 9]
                    })
                });
                gPoints.addLayer(m);
            }
        }

        for (const tr of (state.tracks || [])) {
            if (!tr.pts || tr.pts.length < 2) continue;
            const line = L.polyline(tr.pts, {
                color: tr.color,
                weight: 3,
                dashArray: '6 8',
                opacity: 0.8,
                className: 'war-zone bot-track'
            });
            gTracks.addLayer(line);
        }
    }

    window.sanyMap = {
        init: function (dotnetRef, cfg) {
            dotnet = dotnetRef;
            map = L.map('map', {
                center: [cfg.lat, cfg.lng],
                zoom: cfg.zoom,
                zoomControl: false,
                attributionControl: true,
                dragging: false,
                touchZoom: false,
                doubleClickZoom: true,
                boxZoom: false,
                keyboard: false,
                scrollWheelZoom: true
            });
            gTerritory = gTerr();
            gWars = gTerr();
            gPoints = gTerr();
            gWalk = gTerr();
            gTracks = gTerr();

            const osm = L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
                maxZoom: 19,
                attribution: '© OpenStreetMap contributors',
                className: 'war-tiles'
            });
            const sat = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
                maxZoom: 19,
                attribution: '© Esri, Maxar, Earthstar Geographics'
            });
            osm.addTo(map);

            L.control.layers({ 'Военная схема': osm, 'Спутник': sat }, null, { position: 'bottomleft' }).addTo(map);

            L.control.zoom({ position: 'bottomright' }).addTo(map);

            const el = map.getContainer();
            el.addEventListener('contextmenu', e => e.preventDefault());
            el.addEventListener('mousedown', e => {
                if (e.button === 0) {
                    drawing = true;
                    trackLine = L.polyline([], {
                        color: '#f0b429', weight: 3.5, dashArray: '7 7', opacity: 0.95
                    }).addTo(gWalk);
                    hud('МАРШ · 0 м · замкните петлю ЛКМ зажатой');
                } else if (e.button === 2) {
                    panDrag = true;
                    prevPan = { x: e.clientX, y: e.clientY };
                    el.style.cursor = 'grabbing';
                }
            });

            window.addEventListener('mousemove', e => {
                if (panDrag && map) {
                    const dx = e.clientX - prevPan.x;
                    const dy = e.clientY - prevPan.y;
                    prevPan = { x: e.clientX, y: e.clientY };
                    map.panBy([-dx, -dy], { animate: false });
                    return;
                }
                if (drawing && trackLine && map) {
                    const p = map.mouseEventToLatLng(e);
                    const latlngs = trackLine.getLatLngs();
                    const last = latlngs.length ? latlngs[latlngs.length - 1] : null;
                    if (!last || map.distance(last, p) > 4) {
                        trackLine.addLatLng(p);
                        let meters = 0;
                        for (let i = 1; i < latlngs.length; i++) meters += map.distance(latlngs[i - 1], latlngs[i]);
                        hud(`МАРШ · ${Math.round(meters)} м · ~${Math.floor(meters / 25)} солдат · замкните петлю или вернитесь к своей границе`);
                    }
                }
            });

            window.addEventListener('mouseup', e => {
                if (panDrag) {
                    panDrag = false;
                    el.style.cursor = '';
                }
                if (drawing) {
                    drawing = false;
                    const pts = trackLine ? trackLine.getLatLngs().map(a => [a.lat, a.lng]) : [];
                    if (trackLine) {
                        gWalk.removeLayer(trackLine);
                        trackLine = null;
                    }
                    hud(null);
                    if (pts.length >= 6) dotnet.invokeMethodAsync('OnTrackCompleted', pts);
                }
            });
        },

        pushState: function (json) {
            try { render(JSON.parse(json)); } catch (e) { console.error(e); }
        },

        flyTo: function (lat, lng, zoom) {
            if (map) map.flyTo([lat, lng], zoom || map.getZoom(), { duration: 0.8 });
        }
    };
})();
