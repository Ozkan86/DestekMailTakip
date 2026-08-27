// Basit, bagimliliksiz emoji secici. Bir textarea + tetikleyici butona baglanir,
// secilen emojiyi textarea'da imlecin oldugu yere ekler.
(function () {
    function initEmojiPicker(triggerBtn, textarea, panel) {
        let built = false;
        let activeCategory = 0;

        // Panel, overflow:hidden olan compose-box'in disina cikabilsin diye
        // body'nin en sonuna tasinip position:fixed ile konumlandirilir.
        document.body.appendChild(panel);
        panel.style.position = "fixed";

        function buildPanel() {
            if (built) return;
            built = true;

            const tabsHtml = EMOJI_CATEGORIES
                .map((cat, i) => `<button type="button" class="emoji-tab${i === 0 ? " active" : ""}" data-index="${i}" title="${cat.name}">${cat.icon}</button>`)
                .join("");

            panel.innerHTML = `
                <div class="emoji-picker-tabs">${tabsHtml}</div>
                <input type="text" class="emoji-search" placeholder="Emoji ara..." />
                <div class="emoji-grid"></div>
            `;

            const grid = panel.querySelector(".emoji-grid");
            const search = panel.querySelector(".emoji-search");

            function renderCategory(index) {
                grid.innerHTML = "";
                EMOJI_CATEGORIES[index].emojis.forEach(e => grid.appendChild(makeEmojiButton(e)));
            }

            function makeEmojiButton(e) {
                const btn = document.createElement("button");
                btn.type = "button";
                btn.className = "emoji-item";
                btn.textContent = e;
                btn.addEventListener("click", () => insertEmoji(textarea, e));
                return btn;
            }

            panel.querySelectorAll(".emoji-tab").forEach(tab => {
                tab.addEventListener("click", () => {
                    panel.querySelectorAll(".emoji-tab").forEach(t => t.classList.remove("active"));
                    tab.classList.add("active");
                    activeCategory = parseInt(tab.dataset.index, 10);
                    search.value = "";
                    renderCategory(activeCategory);
                });
            });

            search.addEventListener("input", () => {
                const term = search.value.trim();
                if (!term) {
                    renderCategory(activeCategory);
                    return;
                }
                grid.innerHTML = "";
                EMOJI_CATEGORIES.forEach(cat => {
                    if (cat.name.toLowerCase().includes(term.toLowerCase())) {
                        cat.emojis.forEach(e => grid.appendChild(makeEmojiButton(e)));
                    }
                });
            });

            renderCategory(0);
        }

        function positionPanel() {
            const rect = triggerBtn.getBoundingClientRect();
            const viewportW = document.documentElement.clientWidth;
            const viewportH = document.documentElement.clientHeight;
            const panelWidth = 280;
            const panelMaxHeight = 340;

            // Once gorunur yap ki gercek yuksekligini olcebilelim.
            panel.style.visibility = "hidden";
            panel.classList.add("open");
            const panelHeight = panel.offsetHeight || panelMaxHeight;

            let left = rect.left;
            if (left + panelWidth > viewportW - 8) {
                left = viewportW - panelWidth - 8;
            }
            if (left < 8) {
                left = 8;
            }

            const spaceBelow = viewportH - rect.bottom;
            let top;
            if (spaceBelow >= panelHeight + 8 || spaceBelow >= rect.top) {
                top = rect.bottom + 6;
            } else {
                top = rect.top - panelHeight - 6;
            }

            // Son guvenlik: panel, butonun konumu ne olursa olsun her zaman
            // gorunur alanin (viewport) icinde kalsin; asla alttan/ustten tasmasin.
            const maxTop = Math.max(8, viewportH - panelHeight - 8);
            top = Math.min(Math.max(top, 8), maxTop);

            panel.style.left = left + "px";
            panel.style.top = top + "px";
            panel.style.visibility = "";
        }

        function insertEmoji(target, emoji) {
            const start = target.selectionStart ?? target.value.length;
            const end = target.selectionEnd ?? target.value.length;
            target.value = target.value.slice(0, start) + emoji + target.value.slice(end);
            const cursor = start + emoji.length;
            target.focus();
            target.setSelectionRange(cursor, cursor);
        }

        triggerBtn.addEventListener("click", (ev) => {
            ev.preventDefault();
            const wasOpen = panel.classList.contains("open");
            buildPanel();
            if (wasOpen) {
                panel.classList.remove("open");
            } else {
                positionPanel();
            }
        });

        window.addEventListener("resize", () => {
            if (panel.classList.contains("open")) {
                positionPanel();
            }
        });

        document.addEventListener("click", (ev) => {
            if (!panel.contains(ev.target) && ev.target !== triggerBtn) {
                panel.classList.remove("open");
            }
        });
    }

    window.initEmojiPicker = initEmojiPicker;
})();
