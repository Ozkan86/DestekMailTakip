// ============================================================================
// UiDialog: uygulama geneli onay / metin girisi / uyari penceresi.
//
// Neden var: window.confirm, window.prompt ve window.alert kutulari sayfanin
// TEPESINDEN iner, "localhost:5144 web sitesinin mesaji:" gibi teknik bir
// baslik gosterir, bicimlendirilemez ve tarayiciya gore farkli gorunur.
// Ayrica iframe icinden cagrildiklarinda kullaniciyi hangi pencerenin
// sordugu konusunda yaniltirlar. Buradaki karsiliklari sayfanin ortasinda,
// projenin gorsel diliyle acilir ve Promise dondurur.
//
// Kullanim:
//   UiDialog.confirm({ title: 'Maili sil', message: '...', tone: 'danger' })
//       .then(function (ok) { if (ok) { ... } });
//   UiDialog.prompt({ title: '...', label: '...', required: true })
//       .then(function (value) { if (value !== null) { ... } });
//   UiDialog.alert({ message: '...' });
//   UiDialog.toast({ message: '...', tone: 'error' });
//
// Bildirimli (declarative) kullanim -- bir form gonderilmeden once onay ister:
//   <form ... data-ui-confirm="Emin misiniz?"
//              data-ui-confirm-title="Maili sil"
//              data-ui-confirm-ok="Sil"
//              data-ui-confirm-tone="danger">
//
// ...ya da once bir aciklama ister ve girileni forma yazar:
//   <form ... data-ui-prompt="Kart geri gonderilecek."
//              data-ui-prompt-title="Kartı reddet"
//              data-ui-prompt-label="Reddetme sebebi"
//              data-ui-prompt-field="Note"      (varsayilan: "note")
//              data-ui-prompt-ok="Reddet"
//              data-ui-prompt-tone="warning">
//     <input type="hidden" name="Note" />
// Aciklama varsayilan olarak cok satirli ve zorunludur; data-ui-prompt-multiline
// ve data-ui-prompt-required "false" ile kapatilabilir. Alan formda yoksa gizli
// bir input olarak eklenir.
//
// Her iki durumda da onay verilirse form, sanki kullanici kendisi gondermis
// gibi yeniden gonderilir (requestSubmit); boylece sayfadaki diger submit
// dinleyicileri (or. Mail/Index'te secili mail Id'lerini ekleyen kod, jenerik
// panolarda formu AJAX ile gonderen kod) calismaya devam eder.
// ============================================================================
(function () {
    'use strict';

    var ICONS = {
        danger: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M3 6h18" /><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2" /><line x1="10" y1="11" x2="10" y2="17" /><line x1="14" y1="11" x2="14" y2="17" /></svg>',
        warning: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" /><line x1="12" y1="9" x2="12" y2="13" /><line x1="12" y1="17" x2="12.01" y2="17" /></svg>',
        primary: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10" /><line x1="12" y1="8" x2="12" y2="12" /><line x1="12" y1="16" x2="12.01" y2="16" /></svg>'
    };

    var TOAST_ICONS = {
        error: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10" /><line x1="15" y1="9" x2="9" y2="15" /><line x1="9" y1="9" x2="15" y2="15" /></svg>',
        warning: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" /><line x1="12" y1="9" x2="12" y2="13" /><line x1="12" y1="17" x2="12.01" y2="17" /></svg>',
        success: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5" /></svg>',
        info: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10" /><line x1="12" y1="16" x2="12" y2="12" /><line x1="12" y1="8" x2="12.01" y2="8" /></svg>'
    };

    var FOCUSABLE = 'button:not([disabled]), input:not([disabled]), textarea:not([disabled]), select:not([disabled]), a[href]';

    // Ayni anda birden fazla kutu acilmasin: her yeni istek sirasini bekler.
    var queue = Promise.resolve();

    function tone(value) {
        return value === 'danger' || value === 'warning' ? value : 'primary';
    }

    // Ust pencere ayni kaynaktaysa (mail onizleme iframe'i gibi) kutuyu orada
    // acar; boylece karartma tum ekrani kaplar ve kutu dar cercevenin icine
    // sikismaz. Farkli kaynakli bir cercevede erisim hata firlatir; o zaman
    // kendi penceremizde aciyoruz.
    function hostWindow() {
        var win = window;
        try {
            while (win.parent && win.parent !== win) {
                var parentWin = win.parent;
                if (!parentWin.document || !parentWin.UiDialog || !parentWin.UiDialog._open) {
                    break;
                }
                win = parentWin;
            }
        } catch (e) {
            return window;
        }
        return win;
    }

    function delegate(method, options) {
        var host = hostWindow();
        if (host !== window && host.UiDialog && host.UiDialog[method]) {
            // Diger realm'in Promise'i de thenable oldugu icin sarmalayarak
            // her zaman bu realm'in Promise'ini donduruyoruz.
            return Promise.resolve(host.UiDialog[method](options));
        }
        return null;
    }

    function buildDialog(options) {
        var backdrop = document.createElement('div');
        backdrop.className = 'ui-dialog-backdrop';

        var dialog = document.createElement('div');
        dialog.className = 'ui-dialog ui-dialog-tone-' + options.tone;
        dialog.setAttribute('role', 'dialog');
        dialog.setAttribute('aria-modal', 'true');

        var head = document.createElement('div');
        head.className = 'ui-dialog-head';

        var icon = document.createElement('div');
        icon.className = 'ui-dialog-icon';
        icon.setAttribute('aria-hidden', 'true');
        icon.innerHTML = ICONS[options.tone] || ICONS.primary;
        head.appendChild(icon);

        var headText = document.createElement('div');
        headText.className = 'ui-dialog-head-text';

        var titleEl = document.createElement('h2');
        titleEl.className = 'ui-dialog-title';
        titleEl.textContent = options.title;
        headText.appendChild(titleEl);
        dialog.setAttribute('aria-label', options.title);

        if (options.message) {
            var messageEl = document.createElement('p');
            messageEl.className = 'ui-dialog-message';
            messageEl.textContent = options.message;
            headText.appendChild(messageEl);
        }

        head.appendChild(headText);
        dialog.appendChild(head);

        var input = null;
        var errorEl = null;
        if (options.kind === 'prompt') {
            var body = document.createElement('div');
            body.className = 'ui-dialog-body';

            input = document.createElement(options.multiline ? 'textarea' : 'input');
            input.className = 'ui-dialog-input';
            if (options.multiline) {
                input.rows = 3;
            } else {
                input.type = 'text';
            }
            input.value = options.defaultValue || '';
            if (options.placeholder) {
                input.placeholder = options.placeholder;
            }

            if (options.label) {
                var labelEl = document.createElement('label');
                labelEl.className = 'ui-dialog-label';
                labelEl.textContent = options.label;
                var inputId = 'uiDialogInput' + Date.now();
                input.id = inputId;
                labelEl.htmlFor = inputId;
                body.appendChild(labelEl);
            }

            body.appendChild(input);

            errorEl = document.createElement('p');
            errorEl.className = 'ui-dialog-error';
            errorEl.setAttribute('role', 'alert');
            body.appendChild(errorEl);

            dialog.appendChild(body);
        }

        var foot = document.createElement('div');
        foot.className = 'ui-dialog-foot';

        var cancelBtn = null;
        if (options.kind !== 'alert') {
            cancelBtn = document.createElement('button');
            cancelBtn.type = 'button';
            cancelBtn.className = 'ui-dialog-btn ui-dialog-btn-cancel';
            cancelBtn.textContent = options.cancelText;
            foot.appendChild(cancelBtn);
        }

        var confirmBtn = document.createElement('button');
        confirmBtn.type = 'button';
        confirmBtn.className = 'ui-dialog-btn ui-dialog-btn-confirm';
        confirmBtn.textContent = options.confirmText;
        foot.appendChild(confirmBtn);

        dialog.appendChild(foot);
        backdrop.appendChild(dialog);

        return {
            backdrop: backdrop,
            dialog: dialog,
            input: input,
            errorEl: errorEl,
            cancelBtn: cancelBtn,
            confirmBtn: confirmBtn
        };
    }

    function open(options) {
        var parts = buildDialog(options);
        var previouslyFocused = document.activeElement;
        var settled = false;

        return new Promise(function (resolve) {
            function finish(value) {
                if (settled) {
                    return;
                }
                settled = true;

                document.removeEventListener('keydown', onKeyDown, true);
                parts.backdrop.classList.remove('ui-dialog-open');

                window.setTimeout(function () {
                    if (parts.backdrop.parentNode) {
                        parts.backdrop.parentNode.removeChild(parts.backdrop);
                    }
                    if (!document.querySelector('.ui-dialog-backdrop')) {
                        document.body.classList.remove('ui-dialog-lock');
                    }
                    if (previouslyFocused && typeof previouslyFocused.focus === 'function') {
                        try {
                            previouslyFocused.focus();
                        } catch (e) {
                            /* eleman DOM'dan cikmis olabilir; onemsiz */
                        }
                    }
                    resolve(value);
                }, 160);
            }

            function cancel() {
                finish(options.kind === 'confirm' ? false : (options.kind === 'prompt' ? null : undefined));
            }

            function accept() {
                if (options.kind !== 'prompt') {
                    finish(options.kind === 'confirm' ? true : undefined);
                    return;
                }

                var value = parts.input.value;
                if (options.trim !== false) {
                    value = value.trim();
                }
                if (options.required && !value) {
                    parts.input.classList.add('ui-dialog-input-invalid');
                    parts.errorEl.textContent = options.requiredMessage;
                    parts.input.focus();
                    return;
                }
                parts.input.classList.remove('ui-dialog-input-invalid');
                parts.errorEl.textContent = '';
                finish(value);
            }

            // Tab, kutunun disina cikmasin: modal acikken arkadaki sayfaya
            // klavyeyle gecilebilmesi hem kafa karistirici hem erisilebilirlik
            // acisindan hatali olurdu.
            function trapTab(ev) {
                var focusables = Array.prototype.filter.call(
                    parts.dialog.querySelectorAll(FOCUSABLE),
                    function (el) { return el.offsetParent !== null; }
                );
                if (!focusables.length) {
                    return;
                }
                var first = focusables[0];
                var last = focusables[focusables.length - 1];
                if (ev.shiftKey && document.activeElement === first) {
                    ev.preventDefault();
                    last.focus();
                } else if (!ev.shiftKey && document.activeElement === last) {
                    ev.preventDefault();
                    first.focus();
                }
            }

            function onKeyDown(ev) {
                if (ev.key === 'Escape') {
                    ev.preventDefault();
                    ev.stopPropagation();
                    cancel();
                    return;
                }
                if (ev.key === 'Tab') {
                    trapTab(ev);
                    return;
                }
                if (ev.key === 'Enter') {
                    // Cok satirli girdide Enter yeni satir demektir; onaylamak
                    // icin Ctrl/Cmd+Enter gerekir.
                    if (options.kind === 'prompt' && options.multiline && !(ev.ctrlKey || ev.metaKey)) {
                        return;
                    }
                    if (document.activeElement === parts.cancelBtn) {
                        return;
                    }
                    ev.preventDefault();
                    accept();
                }
            }

            parts.confirmBtn.addEventListener('click', accept);
            if (parts.cancelBtn) {
                parts.cancelBtn.addEventListener('click', cancel);
            }
            parts.backdrop.addEventListener('mousedown', function (ev) {
                if (ev.target === parts.backdrop) {
                    cancel();
                }
            });
            if (parts.input) {
                parts.input.addEventListener('input', function () {
                    parts.input.classList.remove('ui-dialog-input-invalid');
                    parts.errorEl.textContent = '';
                });
            }
            document.addEventListener('keydown', onKeyDown, true);

            document.body.classList.add('ui-dialog-lock');
            document.body.appendChild(parts.backdrop);

            // Acilis animasyonu icin requestAnimationFrame yerine zorlanmis bir
            // yeniden yerlesim (reflow) kullaniliyor: sekme arka plandayken veya
            // pencere gorunur degilken rAF geri cagrilari ertelenir ve kutu
            // gorunmez sekilde acik kalirdi.
            void parts.backdrop.offsetHeight;
            parts.backdrop.classList.add('ui-dialog-open');

            if (parts.input) {
                parts.input.focus();
                parts.input.select();
            } else {
                parts.confirmBtn.focus();
            }
        });
    }

    // Istekleri siraya dizer; onceki kutu kapanmadan yenisi acilmaz.
    function enqueue(options) {
        var result = queue.then(function () {
            return open(options);
        });
        // Sira, hata olsa bile ilerlemeye devam etmeli.
        queue = result.then(function () { }, function () { });
        return result;
    }

    function normalize(options, defaults) {
        var opts = typeof options === 'string' ? { message: options } : (options || {});
        var merged = {
            kind: defaults.kind,
            title: opts.title || defaults.title,
            message: opts.message || '',
            confirmText: opts.confirmText || defaults.confirmText,
            cancelText: opts.cancelText || defaults.cancelText,
            tone: tone(opts.tone || defaults.tone),
            label: opts.label || '',
            placeholder: opts.placeholder || '',
            defaultValue: opts.defaultValue || '',
            multiline: !!opts.multiline,
            required: !!opts.required,
            trim: opts.trim,
            requiredMessage: opts.requiredMessage || 'Bu alan boş bırakılamaz.'
        };
        // Baslik verilmeyip yalnizca mesaj verildiginde mesaji baslik yapmak,
        // ikonun yanindaki bos basligi ve tek satirlik gri metni onler.
        if (!opts.title && opts.message && defaults.kind !== 'prompt') {
            merged.title = opts.message;
            merged.message = '';
        }
        return merged;
    }

    var UiDialog = {
        confirm: function (options) {
            var opts = normalize(options, {
                kind: 'confirm',
                title: 'Emin misiniz?',
                confirmText: 'Onayla',
                cancelText: 'Vazgeç',
                tone: 'primary'
            });
            return delegate('confirm', opts) || enqueue(opts);
        },

        prompt: function (options) {
            var opts = normalize(options, {
                kind: 'prompt',
                title: 'Bilgi girin',
                confirmText: 'Kaydet',
                cancelText: 'Vazgeç',
                tone: 'primary'
            });
            return delegate('prompt', opts) || enqueue(opts);
        },

        alert: function (options) {
            var opts = normalize(options, {
                kind: 'alert',
                title: 'Bilgi',
                confirmText: 'Tamam',
                cancelText: '',
                tone: 'warning'
            });
            return delegate('alert', opts) || enqueue(opts);
        },

        // Bloklamayan bildirim: kullanicidan karar beklemeyen "islem
        // gerceklestirilemedi" turu geri bildirimler icin.
        toast: function (options) {
            var opts = typeof options === 'string' ? { message: options } : (options || {});
            var host = hostWindow();
            if (host !== window && host.UiDialog && host.UiDialog.toast) {
                host.UiDialog.toast(opts);
                return;
            }

            var toneName = TOAST_ICONS[opts.tone] ? opts.tone : 'info';
            var stack = document.querySelector('.ui-toast-stack');
            if (!stack) {
                stack = document.createElement('div');
                stack.className = 'ui-toast-stack';
                stack.setAttribute('aria-live', 'polite');
                document.body.appendChild(stack);
            }

            var toast = document.createElement('div');
            toast.className = 'ui-toast ui-toast-tone-' + toneName;

            var iconEl = document.createElement('span');
            iconEl.className = 'ui-toast-icon';
            iconEl.setAttribute('aria-hidden', 'true');
            iconEl.innerHTML = TOAST_ICONS[toneName];
            toast.appendChild(iconEl);

            var textEl = document.createElement('span');
            textEl.textContent = opts.message || '';
            toast.appendChild(textEl);

            var closeBtn = document.createElement('button');
            closeBtn.type = 'button';
            closeBtn.className = 'ui-toast-close';
            closeBtn.setAttribute('aria-label', 'Bildirimi kapat');
            closeBtn.innerHTML = '&times;';
            toast.appendChild(closeBtn);

            stack.appendChild(toast);
            void toast.offsetHeight;
            toast.classList.add('ui-toast-visible');

            var timer = null;
            function dismiss() {
                if (timer) {
                    window.clearTimeout(timer);
                    timer = null;
                }
                toast.classList.remove('ui-toast-visible');
                window.setTimeout(function () {
                    if (toast.parentNode) {
                        toast.parentNode.removeChild(toast);
                    }
                }, 200);
            }

            closeBtn.addEventListener('click', dismiss);
            timer = window.setTimeout(dismiss, opts.duration || 5000);
        },

        // hostWindow() bir cercevenin bu realm'i kullanip kullanamayacagini
        // bu alana bakarak anlar.
        _open: open
    };

    window.UiDialog = UiDialog;

    // ---- Bildirimli (declarative) form onayi / metin girisi ----

    function confirmOptionsFromDataset(el) {
        var data = el.dataset;
        return {
            title: data.uiConfirmTitle || '',
            message: data.uiConfirm || '',
            confirmText: data.uiConfirmOk || '',
            cancelText: data.uiConfirmCancel || '',
            tone: data.uiConfirmTone || 'primary'
        };
    }

    function promptOptionsFromDataset(el) {
        var data = el.dataset;
        return {
            title: data.uiPromptTitle || '',
            message: data.uiPrompt || '',
            label: data.uiPromptLabel || '',
            placeholder: data.uiPromptPlaceholder || '',
            defaultValue: data.uiPromptDefault || '',
            confirmText: data.uiPromptOk || '',
            cancelText: data.uiPromptCancel || '',
            tone: data.uiPromptTone || 'primary',
            // Bir gerekce kutusunun varsayilani cok satirli ve zorunludur;
            // ihtiyac olursa "false" ile kapatilir.
            multiline: data.uiPromptMultiline !== 'false',
            required: data.uiPromptRequired !== 'false',
            requiredMessage: data.uiPromptRequiredMessage || ''
        };
    }

    // Girilen metni formdaki alana yazar; alan yoksa gizli bir input olusturur.
    function writeValueToField(form, fieldName, value) {
        var field = form.elements[fieldName];
        if (field && field.nodeName) {
            field.value = value;
            return;
        }
        var hidden = document.createElement('input');
        hidden.type = 'hidden';
        hidden.name = fieldName;
        hidden.value = value;
        form.appendChild(hidden);
    }

    // Onaylanan formu, sanki kullanici kendisi gondermis gibi yeniden gonderir;
    // boylece sayfadaki diger submit dinleyicileri (AJAX ile gonderen jenerik
    // pano formlari, secili mail Id'lerini ekleyen kod vb.) calismaya devam eder.
    function resubmit(form, submitter) {
        form.dataset.uiConfirmed = '1';
        if (typeof form.requestSubmit === 'function') {
            form.requestSubmit(submitter);
            return;
        }
        // Eski tarayici: submit olayini elle tetikleyip (dinleyiciler calissin
        // diye) engellenmediyse gonderiyoruz.
        var evt = new Event('submit', { bubbles: true, cancelable: true });
        if (form.dispatchEvent(evt)) {
            form.submit();
        }
    }

    document.addEventListener('submit', function (ev) {
        var form = ev.target;
        if (!form || form.nodeName !== 'FORM') {
            return;
        }

        var isPrompt = form.hasAttribute('data-ui-prompt');
        if (!isPrompt && !form.hasAttribute('data-ui-confirm')) {
            return;
        }

        // Onaylandiktan sonra formu yeniden gonderiyoruz; ikinci turda kutuyu
        // tekrar acmamak icin bayragi burada tuketiyoruz.
        if (form.dataset.uiConfirmed === '1') {
            delete form.dataset.uiConfirmed;
            return;
        }

        ev.preventDefault();
        ev.stopPropagation();

        var submitter = ev.submitter || null;

        if (isPrompt) {
            var fieldName = form.dataset.uiPromptField || 'note';
            UiDialog.prompt(promptOptionsFromDataset(form)).then(function (value) {
                if (value === null) {
                    return;
                }
                writeValueToField(form, fieldName, value);
                resubmit(form, submitter);
            });
            return;
        }

        UiDialog.confirm(confirmOptionsFromDataset(form)).then(function (ok) {
            if (ok) {
                resubmit(form, submitter);
            }
        });
    }, true);
})();
