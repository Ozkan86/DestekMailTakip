// Taslak sablonu seciciyi bir <select> + textarea'ya baglar. Secilen sablonun
// govdesini imlecin bulundugu yere ekler: kutu bossa tasla direkt icerik olur,
// icinde metin varsa imlecin oldugu satirin altina yeni satirla eklenir.
(function () {
    function initDraftTemplatePicker(select, textarea) {
        if (!select || !textarea) {
            return;
        }

        select.addEventListener("change", function () {
            var option = select.options[select.selectedIndex];
            select.selectedIndex = 0;

            if (!option || !option.value) {
                return;
            }

            var body = option.getAttribute("data-body") || "";
            insertTemplate(textarea, body);
        });

        function insertTemplate(target, body) {
            var start = target.selectionStart ?? target.value.length;
            var end = target.selectionEnd ?? target.value.length;
            var prefix = start > 0 ? "\n" : "";
            var text = prefix + body;

            target.value = target.value.slice(0, start) + text + target.value.slice(end);
            var cursor = start + text.length;
            target.focus();
            target.setSelectionRange(cursor, cursor);
        }
    }

    window.initDraftTemplatePicker = initDraftTemplatePicker;
})();
