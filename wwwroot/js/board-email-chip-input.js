// "Yetkili musteri e-postalari" alani icin tek satirlik chip girdisi: yazilan
// her eposta "+" ile (ya da Enter/virgul/noktali virgul ile) bir chip'e donusur.
// Gercek deger, sunucunun beklendigi bicimde (satir satir) gizli textarea'ya
// yazilir; boylece sunucu tarafi hic degismeden calisir. Sayfada birden fazla
// widget olabilir (ör. Panolarim listesinde her pano icin bir tane); bu yuzden
// id yerine [data-email-chip-widget] kok elemani icinde class ile sorgulanir.
(function () {
    function initChipWidget(root) {
        var chipList = root.querySelector('.board-email-chip-list');
        var textInput = root.querySelector('.board-email-chip-textinput');
        var addBtn = root.querySelector('.board-email-chip-add-btn');
        var hiddenField = root.querySelector('textarea');
        var errorEl = root.querySelector('.board-email-chip-error');
        var form = root.closest('form');

        if (!chipList || !textInput || !addBtn || !hiddenField || !errorEl) {
            return;
        }

        var EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
        var emails = [];

        // Model dogrulama hatasi sonrasi sayfa geri donduyse, sunucudan gelen
        // (satir/virgul ayrimli) degeri chip'lere donusturup kutuyu bosaltiyoruz.
        if (hiddenField.value) {
            hiddenField.value.split(/[\r\n,;]+/).map(function (e) { return e.trim(); }).filter(Boolean).forEach(function (e) {
                if (emails.indexOf(e) === -1) {
                    emails.push(e);
                }
            });
            hiddenField.value = '';
        }

        function showError(message) {
            errorEl.textContent = message || '';
        }

        function syncHiddenField() {
            hiddenField.value = emails.join('\n');
        }

        function renderChips() {
            chipList.innerHTML = '';
            emails.forEach(function (email, index) {
                var chip = document.createElement('span');
                chip.className = 'board-email-chip';
                chip.textContent = email;

                var removeBtn = document.createElement('button');
                removeBtn.type = 'button';
                removeBtn.className = 'board-email-chip-remove';
                removeBtn.setAttribute('aria-label', 'Kaldır');
                removeBtn.textContent = '×';
                removeBtn.addEventListener('click', function () {
                    emails.splice(index, 1);
                    renderChips();
                    syncHiddenField();
                });

                chip.appendChild(removeBtn);
                chipList.appendChild(chip);
            });
        }

        function addEmail(raw) {
            var email = (raw || '').trim();
            if (!email) {
                return false;
            }
            if (!EMAIL_RE.test(email)) {
                showError('"' + email + '" geçerli bir e-posta adresi değil.');
                return false;
            }
            var normalized = email.toLowerCase();
            if (emails.some(function (e) { return e.toLowerCase() === normalized; })) {
                showError('"' + email + '" zaten eklendi.');
                return false;
            }
            showError(null);
            emails.push(email);
            return true;
        }

        function addFromInput() {
            var raw = textInput.value;
            var parts = raw.split(/[\s,;]+/).filter(Boolean);
            if (parts.length === 0) {
                return;
            }
            var added = false;
            parts.forEach(function (part) {
                if (addEmail(part)) {
                    added = true;
                }
            });
            if (added) {
                textInput.value = '';
            }
            renderChips();
            syncHiddenField();
        }

        addBtn.addEventListener('click', function () {
            addFromInput();
            textInput.focus();
        });

        textInput.addEventListener('keydown', function (ev) {
            if (ev.key === 'Enter' || ev.key === ',' || ev.key === ';') {
                ev.preventDefault();
                addFromInput();
            } else if (ev.key === 'Backspace' && textInput.value === '' && emails.length > 0) {
                emails.pop();
                renderChips();
                syncHiddenField();
            }
        });

        // Yapistirilan metin birden fazla epostayi (virgul/noktali virgul/bosluk
        // ile ayrilmis) iceriyorsa otomatik olarak chip'lere bolup ekliyoruz;
        // ama sadece "@gmail.com" gibi ayracsiz bir parca yapistirildiysa (ör.
        // yazilan kullanici adinin devamini tamamlamak icin) hicbir ayrac
        // olmadigindan otomatik eklemiyoruz - kullanici elle Enter'a basmali
        // ya da "+" butonuna tiklamali.
        textInput.addEventListener('paste', function (ev) {
            var clipboardData = ev.clipboardData || window.clipboardData;
            var pasted = clipboardData ? clipboardData.getData('text') : '';
            if (/[\s,;]/.test(pasted)) {
                setTimeout(addFromInput, 0);
            }
        });

        // NOT: Kutudan cikildiginda (blur) OTOMATIK EKLEME YAPILMAZ. Eskiden burada
        // bir blur dinleyicisi vardi ve kullanici adresi yazip sayfada bos bir yere
        // tikladiginda adres kendiliginden listeye giriyordu. Ekleme artik yalnizca
        // acik bir niyetle olur: "+" butonu, Enter, virgul/noktali virgul ya da
        // ayracli bir metin yapistirmak. (Form gonderiminde kaybolmamasi icin
        // asagidaki submit dinleyicisi korunuyor.)

        if (form) {
            form.addEventListener('submit', function () {
                if (textInput.value.trim()) {
                    addFromInput();
                }
            });
        }

        // AJAX ile gonderim basarili olduktan sonra (sayfa yenilenmeden)
        // widget'i bosaltmak icin disaridan tetiklenebilen ozel olay.
        root.addEventListener('board-email-chip-reset', function () {
            emails = [];
            textInput.value = '';
            showError(null);
            renderChips();
            syncHiddenField();
        });

        renderChips();
        syncHiddenField();
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-email-chip-widget]').forEach(initChipWidget);
    });
})();
