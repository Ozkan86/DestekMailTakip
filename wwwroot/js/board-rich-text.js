// Kart aciklamasi ve yorumlarda kullanilan, contenteditable tabanli basit
// zengin metin kutusu. document.execCommand eski/deprecated bir API olsa da
// hala tum tarayicilarda calisiyor ve harici bir kutuphane gerektirmiyor.
window.BoardRichText = (function () {
    function toggleInlineCode(contentEl) {
        var sel = window.getSelection();
        if (!sel || sel.rangeCount === 0 || sel.isCollapsed) {
            return;
        }
        var range = sel.getRangeAt(0);

        var node = range.commonAncestorContainer;
        var el = node.nodeType === 1 ? node : node.parentElement;
        var codeParent = el ? el.closest('code') : null;

        if (codeParent && contentEl.contains(codeParent)) {
            var parent = codeParent.parentNode;
            while (codeParent.firstChild) {
                parent.insertBefore(codeParent.firstChild, codeParent);
            }
            parent.removeChild(codeParent);
            return;
        }

        var code = document.createElement('code');
        try {
            range.surroundContents(code);
        } catch (e) {
            var contents = range.extractContents();
            code.appendChild(contents);
            range.insertNode(code);
        }
    }

    // Pencere acilinca odak contenteditable'dan cikip girdi kutusuna gecer ve
    // secim kaybolur; bu yuzden aralik once saklanir, pencere kapaninca geri
    // yuklenir. Aksi halde execCommand('createLink') hicbir metne uygulanmaz.
    function insertLink(content) {
        var selection = window.getSelection();
        var savedRange = (selection && selection.rangeCount) ? selection.getRangeAt(0).cloneRange() : null;

        UiDialog.prompt({
            title: 'Bağlantı ekle',
            message: 'Seçili metnin bağlanacağı adresi girin.',
            label: 'Bağlantı adresi',
            defaultValue: 'https://',
            placeholder: 'https://ornek.com',
            required: true,
            requiredMessage: 'Bir bağlantı adresi girin.',
            confirmText: 'Ekle'
        }).then(function (url) {
            if (!url) {
                return;
            }

            content.focus();
            if (savedRange) {
                var sel = window.getSelection();
                sel.removeAllRanges();
                sel.addRange(savedRange);
            }
            document.execCommand('createLink', false, url);
            content.dispatchEvent(new Event('input', { bubbles: true }));
        });
    }

    function getAntiForgeryToken() {
        var form = document.getElementById('boardAntiForgeryForm');
        var input = form && form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    // Resim ikonu bilgisayardan dosya secmeyi acar; secilen dosya hemen sunucuya
    // yuklenir ve donen gercek URL contenteditable icine <img> olarak eklenir
    // (data: URI kullanilmiyor ki sunucudaki HTML sanitizer'i degistirmeye gerek kalmasin).
    function insertImage(content) {
        var context = content.closest('[data-board-id][data-card-id]');
        if (!context) {
            return;
        }

        var input = document.createElement('input');
        input.type = 'file';
        input.accept = 'image/*';
        input.style.display = 'none';
        document.body.appendChild(input);

        input.addEventListener('change', function () {
            var file = input.files && input.files[0];
            document.body.removeChild(input);
            if (!file) {
                return;
            }

            var fd = new FormData();
            fd.set('__RequestVerificationToken', getAntiForgeryToken());
            fd.set('boardId', context.dataset.boardId);
            fd.set('cardId', context.dataset.cardId);
            fd.set('file', file);

            fetch('/Board/UploadRichTextImage', { method: 'POST', body: fd })
                .then(function (r) { return r.ok ? r.json() : null; })
                .then(function (data) {
                    if (!data) {
                        return;
                    }
                    content.focus();
                    document.execCommand('insertImage', false, data.url);
                    content.dispatchEvent(new Event('input', { bubbles: true }));
                });
        });

        input.click();
    }

    function initOne(rte) {
        if (rte.dataset.rteInit) {
            return;
        }
        rte.dataset.rteInit = '1';

        var content = rte.querySelector('.board-rte-content');
        var toolbar = rte.querySelector('.board-rte-toolbar');
        if (!content || !toolbar) {
            return;
        }

        var formatSelect = toolbar.querySelector('.board-rte-format-select');
        if (formatSelect) {
            formatSelect.addEventListener('mousedown', function (ev) {
                ev.stopPropagation();
            });
            formatSelect.addEventListener('change', function () {
                content.focus();
                document.execCommand('formatBlock', false, '<' + formatSelect.value + '>');
                // Secili secenek burada kalir; secim listesi kapaninca uzerinde
                // secilen bicimin adi (Baslik 1 vb.) gorunmeye devam eder.
                content.dispatchEvent(new Event('input', { bubbles: true }));
            });
        }

        toolbar.querySelectorAll('.board-rte-btn').forEach(function (btn) {
            // Butona mousedown aninda contenteditable odagini/segimini
            // kaybetmemek icin varsayilan davranis engelleniyor.
            btn.addEventListener('mousedown', function (ev) {
                ev.preventDefault();
            });
            btn.addEventListener('click', function () {
                content.focus();
                var cmd = btn.dataset.cmd;
                if (cmd === 'code') {
                    toggleInlineCode(content);
                } else if (cmd === 'link') {
                    insertLink(content);
                } else if (cmd === 'image') {
                    insertImage(content);
                } else {
                    document.execCommand(cmd, false, null);
                }
                content.dispatchEvent(new Event('input', { bubbles: true }));
            });
        });
    }

    function init(root) {
        (root || document).querySelectorAll('.board-rte').forEach(initOne);
    }

    return { init: init };
})();

document.addEventListener('DOMContentLoaded', function () {
    BoardRichText.init(document);
});
