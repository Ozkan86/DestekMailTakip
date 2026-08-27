// Mail eklerinin goruntulenmesi (galeri) ve yanit yazarken birden fazla ek
// secilmesi icin ortak davranislar.
//
// Galeri: konusmadaki tum resim ekleri tek listede toplanir; kucuk resim seridi,
// onceki/sonraki butonlari, klavye oklari ve "3 / 7" konum gostergesi vardir.
// Compose: <input type="file" multiple> her secimde onceki dosyalari silerdi;
// burada secimler biriktirilir (DataTransfer ile input.files yeniden kurulur),
// tek tek kaldirilabilir ve surukle-birak ile de eklenebilir.
(function () {
    'use strict';

    // ---------------------------------------------------------------
    // Ek galerisi
    // ---------------------------------------------------------------
    function initGallery(modal) {
        if (modal.dataset.galleryReady === 'true') {
            return null;
        }

        var thumbs = Array.prototype.slice.call(modal.querySelectorAll('[data-gallery-thumb]'));
        if (thumbs.length === 0) {
            return null;
        }

        var image = modal.querySelector('[data-gallery-image]');
        var nameEl = modal.querySelector('[data-gallery-name]');
        var ownerEl = modal.querySelector('[data-gallery-owner]');
        var positionEl = modal.querySelector('[data-gallery-position]');
        var downloadEl = modal.querySelector('[data-gallery-download]');
        var prevBtn = modal.querySelector('[data-gallery-prev]');
        var nextBtn = modal.querySelector('[data-gallery-next]');
        var thumbsWrap = modal.querySelector('[data-gallery-thumbs]');
        var current = 0;

        var items = thumbs.map(function (thumb) {
            return {
                src: thumb.getAttribute('data-gallery-src'),
                fileName: thumb.getAttribute('data-gallery-filename') || '',
                owner: thumb.getAttribute('data-gallery-owner-name') || ''
            };
        });

        var single = items.length < 2;
        if (prevBtn) { prevBtn.hidden = single; }
        if (nextBtn) { nextBtn.hidden = single; }
        if (thumbsWrap) { thumbsWrap.hidden = single; }

        function show(index) {
            if (items.length === 0) {
                return;
            }

            // Bastan sona ve sondan basa donen dairesel gezinme.
            current = (index + items.length) % items.length;
            var item = items[current];

            if (image) {
                image.setAttribute('src', item.src);
                image.setAttribute('alt', item.fileName);
            }
            if (nameEl) { nameEl.textContent = item.fileName; }
            if (ownerEl) { ownerEl.textContent = item.owner; }
            if (positionEl) { positionEl.textContent = (current + 1) + ' / ' + items.length; }
            if (downloadEl) {
                downloadEl.setAttribute('href', item.src);
                downloadEl.setAttribute('download', item.fileName);
            }

            thumbs.forEach(function (thumb, i) {
                thumb.classList.toggle('is-active', i === current);
            });

            var activeThumb = thumbs[current];
            if (activeThumb && activeThumb.scrollIntoView) {
                activeThumb.scrollIntoView({ block: 'nearest', inline: 'nearest' });
            }
        }

        thumbs.forEach(function (thumb, i) {
            thumb.addEventListener('click', function () {
                show(i);
            });
        });

        if (prevBtn) {
            prevBtn.addEventListener('click', function () { show(current - 1); });
        }
        if (nextBtn) {
            nextBtn.addEventListener('click', function () { show(current + 1); });
        }

        modal.addEventListener('keydown', function (event) {
            if (event.key === 'ArrowLeft') {
                event.preventDefault();
                show(current - 1);
            } else if (event.key === 'ArrowRight') {
                event.preventDefault();
                show(current + 1);
            }
        });

        modal.dataset.galleryReady = 'true';
        modal.galleryShow = show;
        show(0);
        return show;
    }

    function openGallery(modalId, index) {
        var modal = document.getElementById(modalId);
        if (!modal) {
            return;
        }

        initGallery(modal);
        if (typeof modal.galleryShow === 'function') {
            modal.galleryShow(index);
        }

        if (window.bootstrap && window.bootstrap.Modal) {
            window.bootstrap.Modal.getOrCreateInstance(modal).show();
        }
    }

    // ---------------------------------------------------------------
    // Yanit yazarken coklu ek secimi
    // ---------------------------------------------------------------
    function formatSize(bytes) {
        if (bytes < 1024) { return bytes + ' B'; }
        if (bytes < 1024 * 1024) { return (bytes / 1024).toFixed(0) + ' KB'; }
        return (bytes / (1024 * 1024)).toFixed(1) + ' MB';
    }

    function initCompose(options) {
        var input = options.input;
        var trigger = options.trigger;
        var list = options.list;
        var dropZone = options.dropZone;
        var maxTotalBytes = options.maxTotalBytes || 45 * 1024 * 1024;

        // Secilen dosyalar burada birikir; input.files her seferinde bu listeden
        // yeniden kurulur, boylece ikinci kez dosya secmek oncekileri silmez.
        var selected = [];

        function syncInput() {
            var transfer = new DataTransfer();
            selected.forEach(function (file) {
                transfer.items.add(file);
            });
            input.files = transfer.files;
        }

        function totalBytes() {
            return selected.reduce(function (sum, file) { return sum + file.size; }, 0);
        }

        function isSameFile(a, b) {
            return a.name === b.name && a.size === b.size && a.lastModified === b.lastModified;
        }

        function render() {
            list.innerHTML = '';
            if (selected.length === 0) {
                return;
            }

            selected.forEach(function (file) {
                var chip = document.createElement('span');
                chip.className = 'compose-attachment-chip';

                if (file.type && file.type.indexOf('image/') === 0) {
                    var preview = document.createElement('img');
                    preview.className = 'compose-attachment-thumb';
                    preview.alt = file.name;
                    preview.src = URL.createObjectURL(file);
                    preview.addEventListener('load', function () {
                        URL.revokeObjectURL(preview.src);
                    });
                    // Onizlemesi olusturulamayan dosyada kirik resim ikonu
                    // gostermek yerine yalnizca dosya adiyla devam et.
                    preview.addEventListener('error', function () {
                        preview.remove();
                    });
                    chip.appendChild(preview);
                }

                var label = document.createElement('span');
                label.className = 'compose-attachment-name';
                label.textContent = file.name;
                chip.appendChild(label);

                var size = document.createElement('span');
                size.className = 'compose-attachment-size';
                size.textContent = formatSize(file.size);
                chip.appendChild(size);

                var remove = document.createElement('button');
                remove.type = 'button';
                remove.className = 'compose-attachment-remove';
                remove.title = 'Eki kaldır';
                remove.innerHTML = '&times;';
                // Sirasal indeks yerine dosya referansiyla siliniyor; liste her
                // degisiklikte yeniden ciziliyor ve indeks kaymasi olusabiliyor.
                remove.addEventListener('click', function () {
                    selected = selected.filter(function (item) { return item !== file; });
                    syncInput();
                    render();
                });
                chip.appendChild(remove);

                list.appendChild(chip);
            });

            var summary = document.createElement('span');
            summary.className = 'compose-attachment-summary';
            summary.textContent = selected.length + ' ek · ' + formatSize(totalBytes());
            list.appendChild(summary);
        }

        function add(files) {
            var rejectedDuplicates = 0;
            var rejectedSize = false;

            Array.prototype.forEach.call(files, function (file) {
                if (file.size === 0) {
                    return;
                }
                if (selected.some(function (existing) { return isSameFile(existing, file); })) {
                    rejectedDuplicates++;
                    return;
                }
                if (totalBytes() + file.size > maxTotalBytes) {
                    rejectedSize = true;
                    return;
                }
                selected.push(file);
            });

            syncInput();
            render();

            if (rejectedSize) {
                notify('Toplam ek boyutu ' + formatSize(maxTotalBytes) + ' sınırını aştığı için bazı dosyalar eklenmedi.');
            } else if (rejectedDuplicates > 0) {
                notify(rejectedDuplicates + ' dosya zaten ekli olduğu için tekrar eklenmedi.');
            }
        }

        function notify(message) {
            if (window.UiDialog && typeof window.UiDialog.alert === 'function') {
                window.UiDialog.alert(message);
            } else {
                window.alert(message);
            }
        }

        if (trigger) {
            trigger.addEventListener('click', function () {
                input.click();
            });
        }

        input.addEventListener('change', function () {
            if (input.files && input.files.length > 0) {
                // Yeni secimi listeye ekle; input.files syncInput ile yeniden kurulur.
                add(Array.prototype.slice.call(input.files));
            }
        });

        if (dropZone) {
            ['dragenter', 'dragover'].forEach(function (name) {
                dropZone.addEventListener(name, function (event) {
                    event.preventDefault();
                    dropZone.classList.add('is-drop-target');
                });
            });
            ['dragleave', 'drop'].forEach(function (name) {
                dropZone.addEventListener(name, function (event) {
                    event.preventDefault();
                    if (name === 'dragleave' && dropZone.contains(event.relatedTarget)) {
                        return;
                    }
                    dropZone.classList.remove('is-drop-target');
                });
            });
            dropZone.addEventListener('drop', function (event) {
                if (event.dataTransfer && event.dataTransfer.files.length > 0) {
                    add(event.dataTransfer.files);
                }
            });
        }

        // Gonderim sonrasi (veya taslak kaydettikten sonra) sayfa yeniden
        // yuklendiginde liste zaten sifirlanir; ekstra temizlige gerek yok.
        return {
            add: add,
            clear: function () {
                selected = [];
                syncInput();
                render();
            }
        };
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('[data-attachment-gallery]').forEach(initGallery);
    });

    // Galeriyi acan tum tetikleyiciler (ust bardaki resim ikonu ve konusma
    // akisindaki kucuk resimler) tek bir delege dinleyiciyle calisir.
    document.addEventListener('click', function (event) {
        var opener = event.target.closest('[data-gallery-open]');
        if (!opener) {
            return;
        }

        event.preventDefault();
        openGallery(
            opener.getAttribute('data-gallery-target'),
            parseInt(opener.getAttribute('data-gallery-open'), 10) || 0);
    });

    window.initComposeAttachments = initCompose;
    window.openAttachmentGallery = openGallery;
})();
