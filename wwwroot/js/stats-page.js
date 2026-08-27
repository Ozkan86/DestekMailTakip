// "İstatistiklerim" sayfası: çekmece (drawer) panelleri ve özel not alanı.
// Desen, _TopBar.cshtml'deki bildirim panelinin basit toggle+fetch mantığının aynısıdır.
(function () {
    var statsPage = document.querySelector('.stats-page');
    if (!statsPage) {
        return;
    }

    function getToken() {
        var input = statsPage.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function postForm(url, fields) {
        var body = new URLSearchParams();
        body.set('__RequestVerificationToken', getToken());
        Object.keys(fields || {}).forEach(function (key) {
            body.set(key, fields[key]);
        });
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString()
        });
    }

    // --- Çekmeceler (drawer) ---
    var drawerButtons = statsPage.querySelectorAll('.stats-tile-drawer');
    drawerButtons.forEach(function (button) {
        button.addEventListener('click', function () {
            var targetId = button.getAttribute('data-drawer-target');
            var url = button.getAttribute('data-drawer-url');
            var target = document.getElementById(targetId);
            if (!target) {
                return;
            }

            var isOpen = target.classList.contains('open');

            // Baska acik cekmeceleri kapat.
            statsPage.querySelectorAll('.stats-drawer.open').forEach(function (el) {
                if (el !== target) {
                    el.classList.remove('open');
                }
            });
            drawerButtons.forEach(function (btn) { btn.classList.remove('active'); });

            if (isOpen) {
                target.classList.remove('open');
                return;
            }

            button.classList.add('active');
            target.classList.add('open');
            target.innerHTML = '<div class="stats-drawer-loading">Yükleniyor...</div>';

            fetch(url)
                .then(function (r) { return r.text(); })
                .then(function (html) { target.innerHTML = html; })
                .catch(function () { target.innerHTML = '<div class="stats-drawer-empty">Yüklenemedi.</div>'; });
        });
    });

    // --- Notlar ---
    var addBtn = document.getElementById('statsAddNoteTrigger');
    var form = document.getElementById('statsAddNoteForm');
    var cancelBtn = document.getElementById('statsCancelNoteBtn');
    var notesList = document.getElementById('statsNotesList');

    if (addBtn && form) {
        addBtn.addEventListener('click', function () {
            var showing = form.style.display !== 'none';
            form.style.display = showing ? 'none' : '';
            if (!showing) {
                var textarea = form.querySelector('textarea');
                if (textarea) {
                    textarea.focus();
                }
            }
        });
    }

    if (cancelBtn && form) {
        cancelBtn.addEventListener('click', function () {
            form.style.display = 'none';
            var textarea = form.querySelector('textarea');
            if (textarea) {
                textarea.value = '';
            }
        });
    }

    if (form && notesList) {
        form.addEventListener('submit', function (ev) {
            ev.preventDefault();
            var textarea = form.querySelector('textarea[name="body"]');
            var body = textarea ? textarea.value.trim() : '';
            if (!body) {
                return;
            }

            postForm('/Stats/AddNote', { body: body })
                .then(function (r) { return r.text(); })
                .then(function (html) {
                    notesList.innerHTML = html;
                    textarea.value = '';
                    form.style.display = 'none';
                })
                .catch(function () { });
        });

        notesList.addEventListener('click', function (ev) {
            var deleteBtn = ev.target.closest('.stats-note-delete-btn');
            if (deleteBtn) {
                postForm('/Stats/DeleteNote', { id: deleteBtn.getAttribute('data-note-id') })
                    .then(function (r) { return r.text(); })
                    .then(function (html) { notesList.innerHTML = html; })
                    .catch(function () { });
                return;
            }

            var editBtn = ev.target.closest('.stats-note-edit-btn');
            if (editBtn) {
                beginEditNote(editBtn.closest('.stats-note-item'));
            }
        });

        // --- Not düzenleme (satır içi) ---
        function beginEditNote(item) {
            if (!item || item.classList.contains('editing')) {
                return;
            }
            var content = item.querySelector('.stats-note-content');
            var bodyEl = item.querySelector('.stats-note-body');
            var footerEl = item.querySelector('.stats-note-footer');
            if (!content || !bodyEl || !footerEl) {
                return;
            }

            item.classList.add('editing');
            bodyEl.style.display = 'none';
            footerEl.style.display = 'none';

            var editForm = document.createElement('div');
            editForm.className = 'stats-note-edit-form';
            editForm.innerHTML =
                '<textarea maxlength="2000"></textarea>' +
                '<div class="stats-note-form-actions">' +
                '<button type="button" class="btn-chip btn-chip-primary stats-note-save-btn">Kaydet</button>' +
                '<button type="button" class="btn-chip stats-note-cancel-edit-btn">Vazgeç</button>' +
                '</div>';
            content.appendChild(editForm);

            var textarea = editForm.querySelector('textarea');
            textarea.value = bodyEl.textContent;
            textarea.focus();
            textarea.setSelectionRange(textarea.value.length, textarea.value.length);

            function endEdit() {
                editForm.remove();
                bodyEl.style.display = '';
                footerEl.style.display = '';
                item.classList.remove('editing');
            }

            editForm.querySelector('.stats-note-cancel-edit-btn').addEventListener('click', endEdit);

            function save() {
                var newBody = textarea.value.trim();
                if (!newBody || newBody === bodyEl.textContent) {
                    endEdit();
                    return;
                }

                postForm('/Stats/EditNote', { id: item.getAttribute('data-note-id'), body: newBody })
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (data) {
                        if (data) {
                            bodyEl.textContent = data.body;
                        }
                        endEdit();
                    })
                    .catch(endEdit);
            }

            editForm.querySelector('.stats-note-save-btn').addEventListener('click', save);
            textarea.addEventListener('keydown', function (ev) {
                if (ev.key === 'Enter' && (ev.metaKey || ev.ctrlKey)) {
                    ev.preventDefault();
                    save();
                } else if (ev.key === 'Escape') {
                    ev.preventDefault();
                    endEdit();
                }
            });
        }

        // --- Not sürükle-bırak sıralaması ---
        // Not: sadece tutamaç (::drag-handle) sürüklenebilir; ama tasinan/hedef
        // eleman her zaman en yakin .stats-note-item'dir (closest ile bulunur).
        var draggingItem = null;

        function getToken2() {
            return getToken();
        }

        function persistNoteOrder() {
            var items = notesList.querySelectorAll('.stats-note-item');
            if (items.length === 0) {
                return;
            }
            var body = new URLSearchParams();
            body.set('__RequestVerificationToken', getToken2());
            items.forEach(function (item) {
                body.append('ids', item.getAttribute('data-note-id'));
            });
            fetch('/Stats/ReorderNotes', {
                method: 'POST',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
                body: body.toString()
            }).catch(function () { });
        }

        notesList.addEventListener('dragstart', function (ev) {
            var handle = ev.target.closest('.stats-note-drag-handle');
            if (!handle) {
                ev.preventDefault();
                return;
            }
            var item = handle.closest('.stats-note-item');
            if (!item || item.classList.contains('editing')) {
                ev.preventDefault();
                return;
            }
            draggingItem = item;
            ev.dataTransfer.effectAllowed = 'move';
            try { ev.dataTransfer.setData('text/plain', item.getAttribute('data-note-id') || ''); } catch (e) { }
            // Tarayicinin varsayilan surukleme goruntusu olusturmasi icin bir sonraki
            // event dongusune birak, sonra "dragging" stilini uygula.
            window.setTimeout(function () {
                if (draggingItem) {
                    draggingItem.classList.add('stats-note-dragging');
                }
            }, 0);
        });

        notesList.addEventListener('dragover', function (ev) {
            if (!draggingItem) {
                return;
            }
            ev.preventDefault();
            ev.dataTransfer.dropEffect = 'move';

            var target = ev.target.closest('.stats-note-item');
            if (!target || target === draggingItem || target.parentNode !== notesList) {
                return;
            }

            var rect = target.getBoundingClientRect();
            var before = (ev.clientY - rect.top) < rect.height / 2;
            var newNextSibling = before ? target : target.nextSibling;
            var currentNextSibling = draggingItem.nextSibling;
            if (newNextSibling === draggingItem || currentNextSibling === newNextSibling) {
                return;
            }

            // FLIP: diger notlarin "yer aciyormus gibi" kaymasi icin,
            // taşımadan once/sonra konumlarini karsilastirip ters bir transform
            // uygulayip 0'a animasyonla donduruyoruz.
            var items = Array.prototype.slice.call(notesList.querySelectorAll('.stats-note-item'));
            var firstRects = {};
            items.forEach(function (el, i) { firstRects[i] = el.getBoundingClientRect(); });

            notesList.insertBefore(draggingItem, newNextSibling);

            items.forEach(function (el, i) {
                if (el === draggingItem) {
                    return;
                }
                var first = firstRects[i];
                var last = el.getBoundingClientRect();
                var dy = first.top - last.top;
                if (dy) {
                    el.style.transition = 'none';
                    el.style.transform = 'translateY(' + dy + 'px)';
                    requestAnimationFrame(function () {
                        el.style.transition = 'transform .2s ease';
                        el.style.transform = '';
                    });
                    window.setTimeout(function () {
                        el.style.transition = '';
                    }, 220);
                }
            });
        });

        notesList.addEventListener('drop', function (ev) {
            if (draggingItem) {
                ev.preventDefault();
            }
        });

        function finishDrag() {
            if (!draggingItem) {
                return;
            }
            var el = draggingItem;
            draggingItem = null;
            el.classList.remove('stats-note-dragging');

            // Bırakırken efektli bir "yerine oturma" animasyonu.
            el.classList.add('stats-note-settling');
            window.setTimeout(function () {
                el.classList.remove('stats-note-settling');
            }, 280);

            persistNoteOrder();
        }

        notesList.addEventListener('dragend', finishDrag);
    }
})();
