// Sag ustteki arama kutusu: mail listesindeki gonderen adina (musteri veya
// sistem kullanicisi) gore, Turkce buyuk/kucuk harf duyarsiz canli filtre.
(function () {
    document.addEventListener('DOMContentLoaded', function () {
        var wrapper = document.getElementById('topbarSearchWrapper');
        var trigger = document.getElementById('topbarSearchTrigger');
        var input = document.getElementById('topbarSearchInput');
        if (!wrapper || !trigger || !input) {
            return;
        }

        trigger.addEventListener('click', function (ev) {
            ev.stopPropagation();
            if (wrapper.classList.contains('open')) {
                if (!input.value) {
                    closeSearch();
                } else {
                    input.focus();
                }
                return;
            }
            wrapper.classList.add('open');
            input.focus();
        });

        document.addEventListener('click', function (ev) {
            if (wrapper.classList.contains('open') && !wrapper.contains(ev.target) && !input.value) {
                closeSearch();
            }
        });

        input.addEventListener('input', function () {
            applyFilter(input.value);
        });

        input.addEventListener('keydown', function (ev) {
            if (ev.key === 'Escape') {
                input.value = '';
                applyFilter('');
                closeSearch();
            }
        });

        function closeSearch() {
            wrapper.classList.remove('open');
        }

        function applyFilter(rawTerm) {
            var term = rawTerm.trim().toLocaleLowerCase('tr-TR');
            var items = document.querySelectorAll('.mail-list-item-wrap');
            if (!items.length) {
                return;
            }

            var visibleCount = 0;
            items.forEach(function (item) {
                var senderEl = item.querySelector('.mail-item-sender');
                var sender = senderEl ? senderEl.textContent.toLocaleLowerCase('tr-TR') : '';
                var match = !term || sender.indexOf(term) === 0;
                item.style.display = match ? '' : 'none';
                if (match) {
                    visibleCount++;
                }
            });

            var listPane = document.querySelector('.mail-list-pane');
            var emptyMsg = document.getElementById('mailSearchEmptyMessage');
            if (!listPane) {
                return;
            }

            if (term && visibleCount === 0) {
                if (!emptyMsg) {
                    emptyMsg = document.createElement('p');
                    emptyMsg.id = 'mailSearchEmptyMessage';
                    emptyMsg.className = 'text-muted p-3 mb-0';
                    emptyMsg.textContent = 'Aramayla eşleşen mail yok.';
                    listPane.appendChild(emptyMsg);
                }
                emptyMsg.style.display = '';
            } else if (emptyMsg) {
                emptyMsg.style.display = 'none';
            }
        }
    });
})();
