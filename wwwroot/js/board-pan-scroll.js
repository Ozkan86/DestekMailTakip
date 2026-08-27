// Pano kolonlari konteynerinde (Klasik'in .board-columns'i ve yeni
// sablonlarin .board-columns--scrollable'i) sadece scroll bar ile degil,
// bos bir noktaya sol klikle basili tutup fareyi sola/saga surukleyerek de
// yatay kaydirma yapilabilmesini saglar. Karti surukleme (HTML5 drag&drop)
// veya herhangi bir buton/form/link ile bir cakisma olmamasi icin, mousedown
// hedefi bir kart, buton, form elemani, link ya da draggable=true bir oge
// ise devreye girmez.
(function () {
    var INTERACTIVE_SELECTOR = '.board-card, button, a, input, textarea, select, form, details, summary, [draggable="true"]';
    var DRAG_THRESHOLD = 4;

    function initContainer(container) {
        var isPointerDown = false;
        var isPanning = false;
        var startX = 0;
        var startScrollLeft = 0;

        container.addEventListener('mousedown', function (ev) {
            if (ev.button !== 0) {
                return;
            }
            if (ev.target && ev.target.closest && ev.target.closest(INTERACTIVE_SELECTOR)) {
                return;
            }

            isPointerDown = true;
            isPanning = false;
            startX = ev.clientX;
            startScrollLeft = container.scrollLeft;
        });

        document.addEventListener('mousemove', function (ev) {
            if (!isPointerDown) {
                return;
            }

            var deltaX = ev.clientX - startX;

            if (!isPanning) {
                if (Math.abs(deltaX) < DRAG_THRESHOLD) {
                    return;
                }
                isPanning = true;
                container.classList.add('js-pan-scrolling');
            }

            ev.preventDefault();
            container.scrollLeft = startScrollLeft - deltaX;
        });

        document.addEventListener('mouseup', function () {
            if (!isPointerDown) {
                return;
            }
            isPointerDown = false;
            if (isPanning) {
                isPanning = false;
                container.classList.remove('js-pan-scrolling');
            }
        });

        container.addEventListener('mouseleave', function () {
            // Konteynerden disari cikilsa bile surukleme document uzerinden
            // devam eder (mouseup document'e bagli); burada ekstra bir sey
            // yapmaya gerek yok, sadece not amacli birakildi.
        });
    }

    function init() {
        document.querySelectorAll('.board-columns, .board-columns--scrollable').forEach(initContainer);
    }

    document.addEventListener('DOMContentLoaded', init);
})();
