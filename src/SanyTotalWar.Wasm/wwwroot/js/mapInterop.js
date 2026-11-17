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

    // отложенный рендер: во время зум-анимации не пересоздаём векторы
    let renderPending = null;
    let zoomingNow = false;
    let lastSelId = null;

    // дифф состояния для фоновых FX-анимаций
    let fxPrimed = false;          // первый кадр — только фиксируем базу, без анимаций
    let seenTerr = new Map();      // id -> { bld, area }
    let seenWars = new Set();      // id активных войн
    let lastWarCenters = new Map();// id -> [lat,lng]

    const PHOS = '#c6f24e', WAR = '#ff5a4e';
    const BLD_GLYPHS = { 'c': ['В', '#c6f24e'], 'f': ['К', '#ff5a4e'], 'a': ['А', '#5bd4e6'], 'b': ['Б', '#5fc98d'] };

    // векторная графика построек (как в досье, один стиль)
    const BUILD_SVG = {
        c: "<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'><path d='M6 21V4h1.6v3H15a.8.8 0 0 1 .62 1.3L13.2 11l2.42 2.7A.8.8 0 0 1 15 15H7.6v6H6Z' fill='currentColor'/><path d='M3.5 21h17v1.6h-17z' fill='currentColor' opacity='.55'/></svg>",
        f: "<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'><path d='M4 21V10h2V7h1.8v3h3.4V7H13v3h3.4V7H18v3h2v11h-5.5v-4a2.5 2.5 0 0 0-5 0v4H4Z' fill='currentColor'/><path d='M2.5 21h19v1.6h-19z' fill='currentColor' opacity='.55'/></svg>",
        a: "<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'><path d='M21 15.5 13.8 11V4.6a1.8 1.8 0 0 0-3.6 0V11L3 15.5v1.9l7.2-2.1v4.6l-2.4 1.6v1.3L12 21.9l4.2.9v-1.3l-2.4-1.6v-4.6l7.2 2.1v-1.9Z' fill='currentColor'/></svg>",
        b: "<svg viewBox='0 0 24 24' xmlns='http://www.w3.org/2000/svg'><path d='M12 2.5 20 6v5.5c0 5.2-3.4 8.7-8 10-4.6-1.3-8-4.8-8-10V6l8-3.5Z' fill='currentColor'/></svg>"
    };

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
        hud('МАРШ · 0 м · пересеки свой след — петля');
    }

    function pushTrackPoint(latlng) {
        if (!trackLine || !map) return;
        const latlngs = trackLine.getLatLngs();
        const last = latlngs.length ? latlngs[latlngs.length - 1] : null;
        if (!last || map.distance(last, latlng) > 4) {
            trackLine.addLatLng(latlng);
            let meters = 0;
            for (let i = 1; i < latlngs.length; i++) meters += map.distance(latlngs[i - 1], latlngs[i]);
            hud(`МАРШ · ${Math.round(meters)} м · ~${Math.floor(meters / 25)} солдат · пересеки свой след или вернись дугой к своей земле`);
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

    function fmtArea(a) {
        if (a >= 0.05) return a.toFixed(2).replace('.', ',') + ' км²';
        const m2 = Math.round(a * 1e6 / 10) * 10;
        return m2 >= 1000
            ? (m2 / 1000).toFixed(1).replace('.', ',') + ' тыс. м²'
            : m2.toLocaleString('ru-RU') + ' м²';
    }

    // краткое досье территории — отдельный тултип, открывается кликом
    let terrTip = null;

    // ---- режим установки постройки (клик из досье или drop с карточки) ----

    let placement = null; // { terrId, type }
    let ghostEl = null;

    function stopPlacement() {
        placement = null;
        if (ghostEl) { ghostEl.remove(); ghostEl = null; }
        map.getContainer().classList.remove('placing');
    }

    function setPlacement(terrId, type) {
        placement = { terrId: terrId, type: type };
        const el = map.getContainer();
        if (!ghostEl) {
            ghostEl = document.createElement('div');
            ghostEl.className = 'placement-ghost';
            el.appendChild(ghostEl);
        }
        ghostEl.innerHTML = `<span class="bld-mark" style="color:${(BLD_GLYPHS[type] || ['?', '#999'])[1]}">${BUILD_SVG[type] || BUILD_SVG.b}</span>`;
        el.classList.add('placing');
    }

    function tryPlaceAt(latlng) {
        if (!placement || !dotnet) return;
        const entry = terrIndex.get(placement.terrId);
        const inside = entry && entry.polys.some(p => p.contains(latlng));
        if (!inside) {
            if (ghostEl) {
                ghostEl.classList.remove('bad');
                void ghostEl.offsetWidth;
                ghostEl.classList.add('bad');
            }
            return;
        }
        const t = placement.terrId, ty = placement.type;
        stopPlacement();
        dotnet.invokeMethodAsync('OnBuildingDropped', latlng.lat, latlng.lng);
    }

    function closeTerrTip() {
        if (terrTip) {
            try { map.removeLayer(terrTip); } catch (e) { }
            terrTip = null;
        }
    }

    function showTerrTip(t, latlng) {
        closeTerrTip();
        terrTip = L.tooltip({
            className: 'war-tip',
            direction: 'top',
            offset: [0, -10],
            opacity: 1,
            interactive: false,
            sticky: false
        }).setContent(terrTipHtml(t)).setLatLng(latlng).addTo(map);
    }

    // краткое досье территории — разметка
    function terrTipHtml(t) {
        const army = t.army ?? 0;
        const limit = t.limit ?? 0;
        const bld = (t.buildings || []).length;
        return `<div class="tip-terr"><div class="tip-terr-name">${t.name}</div>` +
            `<div class="tip-terr-row">Владелец · ${t.owner || '?'}</div>` +
            `<div class="tip-terr-row">${fmtArea(t.area || 0)} · армия ${army}/${limit}</div>` +
            `<div class="tip-terr-row">${bld ? 'постройки: ' + bld : 'построек нет'} </div></div>`;
    }

    function spawnFx(state) {
        const nowTerr = new Map(), nowWars = new Set(), nowCenters = new Map();

        for (const t of state.territories) {
            const c = centroidOf(t);
            if (!c) continue;
            const bld = (t.buildings || []).length;
            const prev = seenTerr.has(t.id) ? seenTerr.get(t.id) : null;
            nowTerr.set(t.id, { bld, area: t.area || 0 });

            if (!fxPrimed) continue;

            if (prev === null) {
                // новая земля: всход кольца + чип «+площадь» внутри территории
                fxMarker(c[0], c[1],
                    `<div class="fx fx-capture"><i></i><i></i><b></b><s>+${fmtArea(t.area || 0)}</s></div>`, 3200);
            } else {
                const d = (t.area || 0) - prev.area;
                if (d > 0.002) {
                    // земля выросла (дуга/слияние)
                    fxMarker(c[0], c[1],
                        `<div class="fx fx-gain"><s>+${fmtArea(d)}</s></div>`, 2300);
                } else if (d < -0.002) {
                    // земля урезана войной/распадом
                    fxMarker(c[0], c[1],
                        `<div class="fx fx-loss"><s>−${fmtArea(-d)}</s></div>`, 2300);
                }
                if (bld > prev.bld) {
                    fxMarker(c[0], c[1], '<div class="fx fx-bld"></div>', 1800);
                }
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
            if (fxPrimed && !seenWars.has(w.id)) {
                // новая война: ударные кольца + залп ⚔
                fxMarker(c[0], c[1],
                    '<div class="fx fx-war"><i></i><i></i><i></i><b></b><s>⚔</s></div>', 3400);
            }
        }

        for (const id of seenWars) {
            if (!nowWars.has(id) && lastWarCenters.has(id)) {
                const c = lastWarCenters.get(id);
                // война завершилась: дуга мира + галочка
                fxMarker(c[0], c[1],
                    '<div class="fx fx-peace"><i></i><s>✓</s></div>', 2500);
            }
        }

        seenTerr = nowTerr;
        seenWars = nowWars;
        lastWarCenters = nowCenters;
        fxPrimed = true;
    }

    // ---- территории: инкрементальное обновление (без пересоздания под курсором) ----

    const terrIndex = new Map(); // id -> { polys, sig, bldMarker, bldKey, data }

    function geoSig(t) {
        const parts = t.parts || [];
        let sig = parts.length + '|';
        for (const part of parts) {
            sig += part.length + '|';
            for (const ring of part) sig += ring.length + ',';
        }
        return sig;
    }

    function updateTerritories(state) {
        const keep = new Set();

        for (const t of state.territories) {
            keep.add(t.id);
            let entry = terrIndex.get(t.id);
            if (!entry) {
                entry = { polys: [], sig: '', bldMarkers: null, bldKey: null, data: null };
                terrIndex.set(t.id, entry);
            }
            entry.data = t;

            const sig = geoSig(t);
            if (sig !== entry.sig) {
                for (const p of entry.polys) gTerritory.removeLayer(p);
                entry.polys = [];
                for (const part of (t.parts || [])) {
                    if (!part.length || !part[0].length) continue;
                    const latlngs = part.map(r => r.map(p => [p[0], p[1]]));
                    const poly = L.polygon(latlngs, {
                        color: t.color,
                        weight: t.mine ? 3.5 : 2,
                        fillColor: t.color,
                        fillOpacity: t.mine ? 0.38 : 0.30,
                        className: 'terr',
                        bubblingMouseEvents: false
                    });
                // досье открывается кликом; в режиме установки клик = «поставить здесь»
                poly.on('click', e => {
                    if (placement) {
                        tryPlaceAt(e.latlng);
                        return;
                    }
                    const d = entry.data;
                    if (d && d.selected) { closeTerrTip(); }
                    else { showTerrTip(d, e.latlng); }
                    dotnet.invokeMethodAsync('OnTerritoryClicked', d.id);
                });
                    gTerritory.addLayer(poly);
                    entry.polys.push(poly);
                }
                entry.sig = sig;
            }

            const mine = !!t.mine;
            for (const p of entry.polys) {
                p.setStyle({
                    color: t.color,
                    weight: mine ? 3.5 : 2,
                    fillColor: t.color,
                    fillOpacity: mine ? 0.38 : 0.30
                });
                if (p._path) p._path.classList.toggle('sel', !!t.selected);
            }

            // постройки: маркеры по точкам установки, обновление при смене набора
            const c = centroidOf(t);
            const bldList = t.buildings || [];
            const bldKey = c ? bldList.map(b => `${b.t}@${b.lat ?? ''},${b.lng ?? ''}`).join('|') : '';

            if (entry.bldMarkers && entry.bldKey !== bldKey) {
                for (const m of entry.bldMarkers) gTerritory.removeLayer(m);
                entry.bldMarkers = null;
            }
            if (c && bldKey && !entry.bldMarkers) {
                entry.bldMarkers = bldList.map(b => {
                    const g = BLD_GLYPHS[b.t] || ['?', '#999'];
                    const pos = (b.lat != null && b.lng != null) ? [b.lat, b.lng] : c;
                    const m = L.marker(pos, {
                        interactive: false,
                        icon: L.divIcon({
                            className: 'bld-mark-wrap',
                            html: `<span class="bld-mark" style="color:${g[1]}">${BUILD_SVG[b.t] || BUILD_SVG.b}</span>`,
                            iconSize: [26, 26],
                            iconAnchor: [13, 24]
                        })
                    });
                    gTerritory.addLayer(m);
                    return m;
                });
                entry.bldKey = bldKey;
            }
        }

        // исчезнувшие земли (слияние/распад): убрать с карты
        for (const [id, entry] of Array.from(terrIndex)) {
            if (!keep.has(id)) {
                for (const p of entry.polys) gTerritory.removeLayer(p);
                for (const m of (entry.bldMarkers || [])) gTerritory.removeLayer(m);
                terrIndex.delete(id);
            }
        }
    }

    // ---- рендер мира ----

    function render(state) {
        if (!map) return;
        // во время зум-анимации не пересоздаём слои — ждем zoomend
        if (zoomingNow) {
            renderPending = state;
            return;
        }
        var selChanged = (state.sel || null) !== lastSelId;
        if (state.version === lastVersion && !selChanged) return;
        lastVersion = state.version;
        lastSelId = state.sel || null;

        gWars.clearLayers();
        gPoints.clearLayers();
        gTracks.clearLayers();

        updateTerritories(state);
        spawnFx(state);

        for (const w of state.wars) {
            const latlngs = ringsToLatLngs(w.rings, []);
            const glow = L.polygon(latlngs, {
                stroke: false,
                fillColor: WAR,
                fillOpacity: 0.16,
                interactive: false,   // зоны не перехватывают клики территорий
                className: 'war-glow'
            });
            const zone = L.polygon(latlngs, {
                color: WAR,
                weight: 2.5,
                dashArray: '9 7',
                fillOpacity: 0.08,
                interactive: false,
                className: 'war-zone'
            });
            gWars.addLayer(glow);
            gWars.addLayer(zone);

            for (const p of (w.points || [])) {
                const cls = p.by ? 'ctrl-point owned' : 'ctrl-point free';
                const style = p.by ? `border-color:${p.color};background:${p.color}33` : '';
                const m = L.marker([p.lat, p.lng], {
                    interactive: false,
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
                // плавный зум: четверть-левелы вместо стука целыми ступенями
                zoomSnap: 0.25,
                zoomDelta: 0.5,
                wheelDebounceTime: 40,
                zoomAnimation: true,
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
                className: 'war-tiles',
                // гладкое отдаление: не роняем старые тайлы посреди анимации,
                // держим широкий буфер за краем видимой области
                keepBuffer: 5,
                updateWhenZooming: false,
                updateWhenIdle: false,
                errorTileUrl: 'data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw=='
            });
            const sat = L.tileLayer('https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}', {
                maxZoom: 19,
                attribution: '© Esri, Maxar, Earthstar Geographics',
                keepBuffer: 5,
                updateWhenZooming: false,
                updateWhenIdle: false,
                errorTileUrl: 'data:image/gif;base64,R0lGODlhAQABAIAAAP///wAAACH5BAEAAAAALAAAAAABAAEAAAICRAEAOw=='
            });
            osm.addTo(map);

            L.control.layers({ 'Ночная съёмка': osm, 'Спутник': sat }, null, { position: 'bottomleft' }).addTo(map);

            L.control.zoom({ position: 'bottomright' }).addTo(map);

            const el = map.getContainer();
            el.style.touchAction = 'none';
            el.addEventListener('contextmenu', e => e.preventDefault());

            // клик по пустому месту карты закрывает досье-тултип / ставит вне зоны
            map.on('click', e => {
                closeTerrTip();
                if (placement) tryPlaceAt(e.latlng);
            });

            // режим установки постройки: призрак следует за курсором, Esc/ПКМ — отмена
            document.addEventListener('keydown', e => {
                if (e.key === 'Escape' && placement) {
                    stopPlacement();
                    dotnet.invokeMethodAsync('OnPlacementCancelled');
                }
            });
            el.addEventListener('contextmenu', e => {
                if (placement && !e.defaultPrevented) {
                    e.preventDefault();
                    stopPlacement();
                    dotnet.invokeMethodAsync('OnPlacementCancelled');
                }
            });
            el.addEventListener('mousemove', e => {
                if (!ghostEl) return;
                const rect = el.getBoundingClientRect();
                ghostEl.style.left = (e.clientX - rect.left) + 'px';
                ghostEl.style.top = (e.clientY - rect.top) + 'px';
            });

            // drag&drop карточек построек из досье прямо на карту
            el.addEventListener('dragover', e => e.preventDefault());
            el.addEventListener('drop', e => {
                e.preventDefault();
                const ll = map.mouseEventToLatLng(e);
                dotnet.invokeMethodAsync('OnBuildingDropped', ll.lat, ll.lng);
            });

            // ховер при зуме не работает; рендер откладывается до конца зума
            map.on('zoomstart', () => {
                zoomingNow = true;
                el.classList.add('zooming');
            });
            map.on('zoomend', () => {
                zoomingNow = false;
                el.classList.remove('zooming');
                // применяем последнее состояние, отложенное из-за зума
                if (renderPending) {
                    const s = renderPending;
                    renderPending = null;
                    render(s);
                }
            });

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

        // включение/выключение режима размещения из UI (Blazor)
        setPlacement: function (terrId, type, on) {
            if (on) setPlacement(terrId, type);
            else stopPlacement();
        },

        pushState: function (json) {
            try { render(JSON.parse(json)); } catch (e) { console.error(e); }
        },

        flyTo: function (lat, lng, zoom) {
            if (map) map.flyTo([lat, lng], zoom || map.getZoom(), { duration: 0.8 });
        }
    };
})();
