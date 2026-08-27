// Mail konusma zincirinin (musterinin ilk maili + tum yanitlar) UZERINE, ana
// sayfada duran bir <canvas> katmaniyla calisan basit, bagimliliksiz
// cizim/isaretleme araci. Noktalar 0-1 araliginda normalize koordinatla
// saklanir; boylece farkli yukseklikteki gorunumlerde (tam sayfa / onizleme)
// dogru olceklenir.
(function () {
    var TOOL_ALPHA = { pen: 1, marker: 0.4 };
    var TOOL_SWATCHES = {
        pen: ["#e11d48", "#2563eb", "#16a34a", "#111827", "#f97316", "#7c3aed"],
        marker: ["#fbbf24", "#4ade80", "#f472b6", "#60a5fa"]
    };
    var TOOL_LABELS = { pen: "Kalem", marker: "İşaretleyici" };

    function initMailAnnotation(config) {
        var canvas = config.canvas;
        var wrap = config.wrap;
        var mailId = config.mailId;
        var token = config.token;
        var toolButtons = config.toolButtons || [];
        var undoBtn = config.undoBtn;
        var clearBtn = config.clearBtn;

        var ctx = canvas.getContext("2d");
        var strokes = [];
        var activeTool = null;
        var drawing = false;
        var currentStroke = null;
        var saveTimer = null;
        var panels = {};

        var toolState = {
            pen: { color: TOOL_SWATCHES.pen[0], width: 3 },
            marker: { color: TOOL_SWATCHES.marker[0], width: 14 }
        };

        function resizeCanvas() {
            var rect = wrap.getBoundingClientRect();
            if (rect.width === 0 || rect.height === 0) {
                return;
            }
            canvas.width = rect.width;
            canvas.height = rect.height;
            redraw();
        }

        function redraw() {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            strokes.forEach(drawStroke);
        }

        function drawStroke(stroke) {
            if (!stroke.points || stroke.points.length < 2) {
                return;
            }
            var color = stroke.color || (toolState[stroke.tool] || toolState.pen).color;
            var width = stroke.width || (toolState[stroke.tool] || toolState.pen).width;
            var alpha = TOOL_ALPHA[stroke.tool] != null ? TOOL_ALPHA[stroke.tool] : 1;
            ctx.save();
            ctx.globalAlpha = alpha;
            ctx.strokeStyle = color;
            ctx.lineWidth = width;
            ctx.lineCap = "round";
            ctx.lineJoin = "round";
            ctx.beginPath();
            stroke.points.forEach(function (p, i) {
                var x = p[0] * canvas.width;
                var y = p[1] * canvas.height;
                if (i === 0) {
                    ctx.moveTo(x, y);
                } else {
                    ctx.lineTo(x, y);
                }
            });
            ctx.stroke();
            ctx.restore();
        }

        function activateTool(tool) {
            activeTool = tool;
            toolButtons.forEach(function (btn) {
                btn.classList.toggle("active", btn.dataset.tool === tool);
            });
            canvas.style.pointerEvents = "auto";
        }

        function deactivateTool() {
            activeTool = null;
            toolButtons.forEach(function (btn) {
                btn.classList.remove("active");
            });
            canvas.style.pointerEvents = "none";
        }

        function ensurePanel(tool) {
            if (panels[tool]) {
                return panels[tool];
            }

            var panel = document.createElement("div");
            panel.className = "annotation-options-panel";
            document.body.appendChild(panel);

            var swatchesHtml = TOOL_SWATCHES[tool]
                .map(function (color) {
                    var selected = toolState[tool].color === color ? " selected" : "";
                    return '<button type="button" class="annotation-color-swatch' + selected + '" data-color="' + color + '" style="background:' + color + ';" title="' + color + '"></button>';
                })
                .join("");

            var thicknessHtml = tool === "pen"
                ? '<div class="annotation-thickness-row">' +
                  '<span>Kalınlık</span>' +
                  '<input type="range" min="1" max="12" step="0.5" value="' + toolState.pen.width + '" class="annotation-thickness-input" style="accent-color:' + toolState.pen.color + ';" />' +
                  '<span class="annotation-thickness-value">' + toolState.pen.width + 'px</span>' +
                  "</div>"
                : "";

            panel.innerHTML =
                '<div class="annotation-options-title">' + TOOL_LABELS[tool] + " rengi</div>" +
                '<div class="annotation-color-row">' + swatchesHtml + "</div>" +
                thicknessHtml;

            panel.querySelectorAll(".annotation-color-swatch").forEach(function (swatch) {
                swatch.addEventListener("click", function () {
                    toolState[tool].color = swatch.dataset.color;
                    panel.querySelectorAll(".annotation-color-swatch").forEach(function (s) {
                        s.classList.toggle("selected", s === swatch);
                    });
                    var thicknessInputEl = panel.querySelector(".annotation-thickness-input");
                    if (thicknessInputEl) {
                        thicknessInputEl.style.accentColor = swatch.dataset.color;
                    }
                    activateTool(tool);
                });
            });

            var thicknessInput = panel.querySelector(".annotation-thickness-input");
            if (thicknessInput) {
                thicknessInput.addEventListener("input", function () {
                    toolState.pen.width = parseFloat(thicknessInput.value);
                    panel.querySelector(".annotation-thickness-value").textContent = thicknessInput.value + "px";
                });
            }

            panels[tool] = panel;
            return panel;
        }

        function isPanelOpen(tool) {
            return panels[tool] && panels[tool].classList.contains("open");
        }

        function closeAllPanels() {
            Object.keys(panels).forEach(function (tool) {
                panels[tool].classList.remove("open");
            });
        }

        function positionPanel(tool, triggerBtn) {
            var panel = panels[tool];
            if (!panel) {
                return;
            }

            var panelRect = panel.getBoundingClientRect();
            var btnRect = triggerBtn.getBoundingClientRect();
            var viewportW = document.documentElement.clientWidth;
            var viewportH = document.documentElement.clientHeight;

            var left = btnRect.left;
            if (left + panelRect.width > viewportW - 8) {
                left = viewportW - panelRect.width - 8;
            }
            if (left < 8) {
                left = 8;
            }

            var top = btnRect.bottom + 6;
            if (top + panelRect.height > viewportH - 8) {
                top = btnRect.top - panelRect.height - 6;
            }

            panel.style.left = left + "px";
            panel.style.top = top + "px";
        }

        function repositionOpenPanels() {
            Object.keys(panels).forEach(function (tool) {
                var panel = panels[tool];
                if (!panel || !panel.classList.contains("open")) {
                    return;
                }
                var btn = toolButtons.find(function (b) { return b.dataset.tool === tool; });
                if (btn) {
                    positionPanel(tool, btn);
                }
            });
        }

        function openPanel(tool, triggerBtn) {
            var panel = ensurePanel(tool);
            closeAllPanels();

            panel.style.visibility = "hidden";
            panel.classList.add("open");
            positionPanel(tool, triggerBtn);
            panel.style.visibility = "";
        }

        function closePanel(tool) {
            if (panels[tool]) {
                panels[tool].classList.remove("open");
            }
        }

        toolButtons.forEach(function (btn) {
            var tool = btn.dataset.tool;
            btn.addEventListener("click", function (ev) {
                ev.preventDefault();
                ev.stopPropagation();

                if (isPanelOpen(tool)) {
                    closePanel(tool);
                    return;
                }

                if (activeTool === tool) {
                    deactivateTool();
                    return;
                }

                openPanel(tool, btn);
            });
        });

        document.addEventListener("click", function (ev) {
            Object.keys(panels).forEach(function (tool) {
                var panel = panels[tool];
                var btn = toolButtons.find(function (b) { return b.dataset.tool === tool; });
                if (panel && panel.classList.contains("open") && !panel.contains(ev.target) && ev.target !== btn) {
                    panel.classList.remove("open");
                }
            });
        });

        // Mail govdesi sandboxed bir iframe; iframe icine tiklamak parent
        // dokumanda hicbir 'click' olayi tetiklemez (ayri dokuman). Odagin
        // iframe'e gectigini (dolayisiyla kullanicinin oraya tikladigini)
        // yakalamak icin parent pencerenin 'blur' olayini kullaniyoruz.
        window.addEventListener("blur", function () {
            closeAllPanels();
        });

        // Ust scroll edilebilir konteyner (.app-content) kaydirildiginda
        // acik panel butonun yanindan kopmasin diye yeniden konumlandir.
        window.addEventListener("scroll", repositionOpenPanels, true);
        window.addEventListener("resize", repositionOpenPanels);

        function pointFromEvent(ev) {
            var rect = canvas.getBoundingClientRect();
            var point = ev.touches && ev.touches.length ? ev.touches[0] : ev;
            return [
                Math.min(Math.max((point.clientX - rect.left) / rect.width, 0), 1),
                Math.min(Math.max((point.clientY - rect.top) / rect.height, 0), 1)
            ];
        }

        function startDraw(ev) {
            if (!activeTool) {
                return;
            }
            ev.preventDefault();
            closeAllPanels();
            drawing = true;
            var style = toolState[activeTool];
            currentStroke = { tool: activeTool, color: style.color, width: style.width, points: [pointFromEvent(ev)] };
        }

        function moveDraw(ev) {
            if (!drawing) {
                return;
            }
            ev.preventDefault();
            currentStroke.points.push(pointFromEvent(ev));
            redraw();
            drawStroke(currentStroke);
        }

        function endDraw() {
            if (!drawing) {
                return;
            }
            drawing = false;
            if (currentStroke.points.length > 1) {
                strokes.push(currentStroke);
                scheduleSave();
            }
            currentStroke = null;
            redraw();
        }

        function scheduleSave() {
            clearTimeout(saveTimer);
            saveTimer = setTimeout(save, 500);
        }

        function save() {
            fetch("/Mail/SaveAnnotations", {
                method: "POST",
                headers: {
                    "Content-Type": "application/x-www-form-urlencoded",
                    "RequestVerificationToken": token
                },
                body: "id=" + encodeURIComponent(mailId) + "&strokesJson=" + encodeURIComponent(JSON.stringify(strokes))
            }).catch(function () { /* sessizce yut: cizim yerel olarak kalir */ });
        }

        function undo() {
            if (!strokes.length) {
                return;
            }
            strokes.pop();
            redraw();
            scheduleSave();
        }

        function clearAll() {
            if (!strokes.length) {
                return;
            }
            UiDialog.confirm({
                title: "İşaretlemeleri temizle",
                message: "Bu mail üzerindeki tüm çizim ve işaretlemeler silinecek.",
                confirmText: "Temizle",
                tone: "danger"
            }).then(function (ok) {
                if (!ok) {
                    return;
                }
                strokes = [];
                redraw();
                scheduleSave();
            });
        }

        if (undoBtn) {
            undoBtn.addEventListener("click", undo);
        }
        if (clearBtn) {
            clearBtn.addEventListener("click", clearAll);
        }

        canvas.addEventListener("mousedown", startDraw);
        canvas.addEventListener("mousemove", moveDraw);
        window.addEventListener("mouseup", endDraw);
        canvas.addEventListener("touchstart", startDraw, { passive: false });
        canvas.addEventListener("touchmove", moveDraw, { passive: false });
        canvas.addEventListener("touchend", endDraw);

        if (window.ResizeObserver) {
            new ResizeObserver(resizeCanvas).observe(wrap);
        } else {
            window.addEventListener("resize", resizeCanvas);
        }
        resizeCanvas();

        fetch("/Mail/GetAnnotations?id=" + encodeURIComponent(mailId))
            .then(function (r) { return r.json(); })
            .then(function (data) {
                try {
                    strokes = JSON.parse(data.strokes) || [];
                } catch (e) {
                    strokes = [];
                }
                redraw();
            })
            .catch(function () { /* baglanti yoksa bos canvas ile devam */ });
    }

    window.initMailAnnotation = initMailAnnotation;
})();
