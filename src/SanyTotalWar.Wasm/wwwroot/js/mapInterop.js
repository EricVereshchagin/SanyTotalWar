(function () {
    let map = null;
    let dotnet = null;
    let trackLine = null;
    let drawing = false;
    let touchDrawing = false;
    let marchMode = false;
    let isTouch = false;
    let panDrag = false;
    let prevPan = null;
    let lastVersion = -1;

    const gTerr = () => L.layerGroup().addTo(map);
    let gTerritory, gWars, gPoints, gWalk, gTracks, gFx;

    // дифф состояния для фоновых FX-анимаций
    let seenTerr = new Map();      // id -> кол-во построек
    let seenWars = new Set();      // id активных войн
    let lastWarCenters = new Map();// id -> [lat,lng]

    const PHOS = '#c6f24e', WAR = '#ff5a4e';
    const BLD_GLYPHS = { 'c': ['В', '#c6f24e'], 'f': ['К', '#ff5a4e'], 'a': ['А', '#5bd4e6'], 'b': ['Б', '#5fc98d'] };

    function dockOffset() {
        const d = document.querySelector('.dock');
        return d ? d.offsetHeight : 0;
    }

    function hud(text) {
        const el = document.getElementById('walk-hud');
        if (!el) return;
        if (!text) { el.style.display = 'none'; return; }
        el.style.display = 'block';
        el.style.bottom = (dockOffset() + 26) + 'px';
        el.textContent = text;
    }

    function ringsToLatLngs(rings, holes) {
        const out = [];
        for (const r of rings) out.push(r.map(p => [p[0], p[1]]));
        for (const h of (holes || [])) out.push(h.map(p => [p[0], p[1]]));
        return out;
    }

    // ---- трек маршрута (общее для мыши и тача) ----

    function startTrack() {
        drawing = true;
        trackLine = L.polyline([], {
            color: PHOS, weight: 3.5, dashArray: '7 7', opacity: 0.95,
            lineCap: 'round'
        }).addTo(gWalk);
        hud('МАРШ · 0 м · замкните петлю');
    }

    function pushTrackPoint(latlng) {
        if (!trackLine || !map) return;
        const latlngs = trackLine.getLatLngs();
        const last = latlngs.length ? latlngs[latlngs.length - 1] : null;
        if (!last || map.distance(last, latlng) > 4) {
            trackLine.addLatLng(latlng);
            let meters = 0;
            for (let i = 1; i < latlngs.length; i++) meters += map.distance(latlngs[i - 1], latlngs[i]);
            hud(`МАРШ · ${Math.round(meters)} м · ~${Math.floor(meters / 25)} солдат · замкните петлю`);
        }
    }

    function cancelTrack() {
        drawing = false;
        if (trackLine) { gWalk.removeLayer(trackLine); trackLine = null; }
        hud(null);
    }

    function finishTrack() {
        drawing = false;
        const pts = trackLine ? trackLine.getLatLngs().map(a => [a.lat, a.lng]) : [];
        if (trackLine) { gWalk.removeLayer(trackLine); trackLine = null; }
        hud(null);
        if (pts.length >= 6) dotnet.invokeMethodAsync('OnTrackCompleted', pts);
    }

    function latlngFromClient(cx, cy) {
        const el = map.getContainer();
        const rect = el.getBoundingClientRect();
        return map.containerPointToLatLng(L.point(cx - rect.left, cy - rect.top));
    }

    // ---- FX: анимации внутри территорий (вместо текстовых тостов) ----

    function fxMarker(lat, lng, html, ttlMs) {
        if (!map) return;
        const m = L.marker([lat, lng], {
            interactive: false,
            icon: L.divIcon({
                className: 'fx-wrap',
                html: html,
                iconSize: [160, 160],
                iconAnchor: [80, 80]
            })
        }).addTo(gFx);
        setTimeout(() => { try { gFx.removeLayer(m); } catch (e) { } }, ttlMs);
    }

    function centroidOf(t) {
        const ring = t.parts && t.parts.length && t.parts[0].length ? t.parts[0][0] : null;
        if (!ring || !ring.length) return null;
        let la = 0, ln = 0;
        for (const p of ring) { la += p[0]; ln += p[1]; }
        return [la / ring.length, ln / ring.length];
    }

    function spawnFx(state) {
        const nowTerr = new Map(), nowWars = new Set(), nowCenters = new Map();

        for (const t of state.territories) {
            const c = centroidOf(t);
            if (!c) continue;
            nowTerr.set(t.id, (t.buildings || []).length);
            const prevBld = seenTerr.get(t.id);
            if (seenTerr.has(t.id)) {
                // рост постройки на живой земле
                if ((t.buildings || []).length > prevBld) {
                    fxMarker(c[0], c[1], '<div class="fx fx-bld"></div>', 1800);
                }
            } else {
                // новая земля — захват (всплеск + штамп-кольцо)
                fxMarker(c[0], c[1], '<div class="fx fx-capture"><i></i><i></i><b></b></div>', 2600);
            }
        }

        for (const w of state.wars) {
            const ring = w.rings && w.rings.length ? w.rings[0] : null;
            if (!ring || !ring.length) continue;
            let la = 0, ln = 0;
            for (const p of ring) { la += p[0]; ln += p[1]; }
            const c = [la / ring.length, ln / ring.length];
            nowWars.add(w.id);
            nowCenters.set(w.id, c);
            if (!seenWars.has(w.id)) {
                // новая война — ударные кольца в зоне
                fxMarker(c[0], c[1], '<div class="fx fx-war"><i></i><i></i><i></i><b></b></div>', 3200);
            }
        }

        for (const id of seenWars) {
            if (!nowWars.has(id) && lastWarCenters.has(id)) {
                const c = lastWarCenters.get(id);
                // война завершилась — мягкая дуга затухания
                fxMarker(c[0], c[1], '<div class="fx fx-peace"><i></i></div>', 2200);
            }
        }

        seenTerr = nowTerr;
        seenWars = nowWars;
        lastWarCenters = nowCenters;
    }

    // ---- рендер мира ----

    function render(state) {
        if (!map) return;
        if (state.version === lastVersion) return;
        lastVersion = state.version;

        gTerritory.clearLayers();
        gWars.clearLayers();
        gPoints.clearLayers();
        gTracks.clearLayers();

        spawnFx(state);

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
                fillColor: WAR,
                fillOpacity: 0.16,
                className: 'war-glow'
            });
            const zone = L.polygon(latlngs, {
                color: WAR,
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
            // настоящий тач: primary input — палец. L.Browser.touch врёт на
            // десктопах с тачскрином, из-за чего ЛКМ начинает панировать.
            isTouch = ('ontouchstart' in window)
                && navigator.maxTouchPoints > 0
                && window.matchMedia('(pointer: coarse)').matches;
            map = L.map('map', {
                center: [cfg.lat, cfg.lng],
                zoom: cfg.zoom,
                zoomControl: false,
                attributionControl: true,
                dragging: isTouch,          // тач: панорама одним пальцем
                touchZoom: isTouch,         // тач: пинч
                doubleClickZoom: true,
                boxZoom: false,
                keyboard: false,
                scrollWheelZoom: true,
                tapHold: true
            });
            gTerritory = gTerr();
            gWars = gTerr();
            gPoints = gTerr();
            gWalk = gTerr();
            gTracks = gTerr();
            gFx = gTerr();

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

            L.control.layers({ 'Ночная съёмка': osm, 'Спутник': sat }, null, { position: 'bottomleft' }).addTo(map);

            L.control.zoom({ position: 'bottomright' }).addTo(map);

            const el = map.getContainer();
            el.style.touchAction = 'none';
            el.addEventListener('contextmenu', e => e.preventDefault());

            // --- мышь: ЛКМ = маршрут, ПКМ = панорама ---
            el.addEventListener('mousedown', e => {
                if (e.button === 0) {
                    startTrack();
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
                if (drawing && trackLine && map) pushTrackPoint(map.mouseEventToLatLng(e));
            });

            window.addEventListener('mouseup', e => {
                if (panDrag) {
                    panDrag = false;
                    el.style.cursor = '';
                }
                if (drawing && e.button === 0) finishTrack();
            });

            // --- тач: режим марша = один палец ведёт маршрут ---
            el.addEventListener('touchstart', e => {
                if (marchMode && e.touches.length === 1 && !touchDrawing) {
                    e.preventDefault();
                    touchDrawing = true;
                    startTrack();
                }
            }, { passive: false });

            el.addEventListener('touchmove', e => {
                if (touchDrawing && marchMode && e.touches.length === 1) {
                    e.preventDefault();
                    const t = e.touches[0];
                    pushTrackPoint(latlngFromClient(t.clientX, t.clientY));
                }
            }, { passive: false });

            const endTouch = e => {
                if (touchDrawing) {
                    e.preventDefault();
                    touchDrawing = false;
                    finishTrack();
                }
            };
            el.addEventListener('touchend', endTouch, { passive: false });
            el.addEventListener('touchcancel', endTouch, { passive: false });
        },

        // включение/выключение режима марша (FAB)
        setMarchMode: function (on) {
            marchMode = on;
            if (!map) return;
            if (on) {
                map.dragging.disable();
                map.touchZoom.disable();
                map.doubleClickZoom.disable();
            } else {
                cancelTrack();
                touchDrawing = false;
                if (isTouch) {
                    map.dragging.enable();
                    map.touchZoom.enable();
                    map.doubleClickZoom.enable();
                }
            }
        },

        pushState: function (json) {
            try { render(JSON.parse(json)); } catch (e) { console.error(e); }
        },

        flyTo: function (lat, lng, zoom) {
            if (map) map.flyTo([lat, lng], zoom || map.getZoom(), { duration: 0.8 });
        }
    };
})();
