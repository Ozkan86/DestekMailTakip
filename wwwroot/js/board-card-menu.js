// Trello benzeri kart duzenleme overlay'i: kart on plana gelir (elevated),
// yaninda navigasyonu tamamen sunucudan fetch edilen partial'lari (options ->
// etiketler -> etiket duzenleyici) panel container'a basan bir JS katmani.
(function () {
    var backdrop = null;
    var panelContainer = null;
    // Panelin hizalandigi kart/tetikleyici dikdortgeni; icerik her degistiginde
    // (ve pencere yeniden boyutlandiginda) panel buna gore tekrar konumlandirilir.
    var panelAnchorRect = null;
    var elevatedCard = null;
    var placeholder = null;

    var detailBackdrop = null;
    var detailModalEl = null;
    var pendingCardChanges = null;
    var pendingIdCounter = 0;

    function getToken() {
        var form = document.getElementById('boardAntiForgeryForm');
        var input = form && form.querySelector('input[name="__RequestVerificationToken"]');
        return input ? input.value : '';
    }

    function postForm(url, fields) {
        var body = new URLSearchParams();
        body.set('__RequestVerificationToken', getToken());
        Object.keys(fields).forEach(function (key) {
            body.set(key, fields[key]);
        });
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString()
        });
    }

    function postFile(url, fields, file) {
        var body = new FormData();
        body.set('__RequestVerificationToken', getToken());
        Object.keys(fields).forEach(function (key) {
            body.set(key, fields[key]);
        });
        body.set('file', file);
        return fetch(url, { method: 'POST', body: body });
    }

    function fetchPanel(url) {
        return fetch(url).then(function (r) { return r.text(); });
    }

    // Kapak degistiren tum endpoint'ler {cardId, coverColor, coverImagePath}
    // sekli JSON doner; kart DOM'undaki serit/banner buna gore guncellenir.
    function applyCardCover(data) {
        var card = document.querySelector('.board-card[data-card-id="' + data.cardId + '"]');
        if (card) {
            var existing = card.querySelector('.board-card-cover');
            if (existing) {
                existing.remove();
            }

            card.classList.toggle('has-cover', !!(data.coverColor || data.coverImagePath));

            if (data.coverColor || data.coverImagePath) {
                var cover = document.createElement('div');
                if (data.coverImagePath) {
                    cover.className = 'board-card-cover board-card-cover-image';
                    cover.style.backgroundImage = "url('" + data.coverImagePath + "')";
                } else {
                    cover.className = 'board-card-cover board-card-cover-color';
                    cover.style.background = data.coverColor;
                }
                // Sunucu tarafi ile ayni sira: kapak, etiket seritlerinin (yoksa
                // basligin) hemen ustunde durur. "ilk cocuktan sonra" demek,
                // Razor ciktisindaki bosluk dugumlerine gore kayabiliyordu.
                card.insertBefore(
                    cover,
                    card.querySelector('.board-card-labels') || card.querySelector('.board-card-title'));
            }
        }

        // Kart detay penceresi ayni kart icin aciksa, kapak orada da guncellensin.
        if (detailModalEl) {
            var inner = detailModalEl.querySelector('.board-card-detail-modal-inner[data-card-id="' + data.cardId + '"]');
            if (inner) {
                var wrap = inner.querySelector('.board-card-detail-cover-wrap');
                var existingDetailCover = wrap.querySelector('.board-card-detail-cover');
                if (existingDetailCover) {
                    existingDetailCover.remove();
                }
                wrap.classList.toggle('no-cover', !(data.coverColor || data.coverImagePath));
                if (data.coverColor || data.coverImagePath) {
                    var detailCover = document.createElement('div');
                    if (data.coverImagePath) {
                        detailCover.className = 'board-card-detail-cover board-card-detail-cover-image';
                        detailCover.style.backgroundImage = "url('" + data.coverImagePath + "')";
                    } else {
                        detailCover.className = 'board-card-detail-cover board-card-detail-cover-color';
                        detailCover.style.background = data.coverColor;
                    }
                    wrap.insertBefore(detailCover, wrap.firstChild);
                }
            }
        }
    }

    function setPanelHtml(html) {
        panelContainer.innerHTML = html;
        // Icerik degistikce (secenekler -> etiketler -> etiket duzenleyici gibi ic
        // gecislerde) panelin yuksekligi de degisir; her seferinde yeniden konumlandir.
        positionPanel();
    }

    function positionPanel() {
        if (!panelContainer || !panelAnchorRect) {
            return;
        }

        var cardRect = panelAnchorRect;
        var viewportW = document.documentElement.clientWidth;
        var viewportH = document.documentElement.clientHeight;
        var panelWidth = 280;
        var margin = 8;

        var left = cardRect.right + 16;
        if (left + panelWidth > viewportW - margin) {
            left = cardRect.left - panelWidth - 16;
        }
        if (left < margin) {
            left = Math.max(margin, viewportW - panelWidth - margin);
        }

        // Panel varsayilan olarak kartin ustuyle hizalanir; ancak asagidaki
        // kartlarda bu, panelin gorunur alanin altindan tasmasina yol aciyordu.
        // Bu yuzden once gercek icerik yuksekligi olculur, sonra panel yukari
        // kaydirilarak tamamen gorunur alanin icinde tutulur. Yalnizca icerik
        // ekrandan gercekten uzunsa (max-height'e dayaninca) kendi icinde kaydirilir.
        var maxHeight = viewportH - margin * 2;
        panelContainer.style.bottom = 'auto';
        panelContainer.style.maxHeight = maxHeight + 'px';
        panelContainer.style.width = panelWidth + 'px';
        panelContainer.style.left = left + 'px';

        var panelHeight = Math.min(panelContainer.scrollHeight, maxHeight);
        var top = Math.max(margin, cardRect.top);
        if (top + panelHeight > viewportH - margin) {
            top = Math.max(margin, viewportH - panelHeight - margin);
        }

        panelContainer.style.top = top + 'px';
    }

    // Kucuk panelin (secenekler/etiketler/kapak) arkaplan+konteynerini
    // olusturur; hem kart-kalemi tiklamasindan (kart on plana alinarak) hem de
    // buyuk kart detay penceresinin icindeki tetikleyicilerden (kart on plana
    // alinmadan) ortak olarak kullanilir.
    function createSmallPanelShell(rect) {
        backdrop = document.createElement('div');
        backdrop.className = 'board-card-overlay-backdrop';
        document.body.appendChild(backdrop);

        panelContainer = document.createElement('div');
        panelContainer.className = 'board-card-menu-panel-container';
        document.body.appendChild(panelContainer);
        panelAnchorRect = rect;
        positionPanel();

        backdrop.addEventListener('click', closeOverlay);
        document.addEventListener('keydown', onKeyDown);
        window.addEventListener('resize', positionPanel);
    }

    // Halihazirda acik bir kucuk panel yoksa tetikleyici elemanin yanina yenisini
    // acar; varsa sadece icerigini degistirir (options -> etiketler gibi ic
    // navigasyonlarla ayni davranis).
    function showSmallPanel(triggerEl, url) {
        if (!panelContainer) {
            createSmallPanelShell(triggerEl.getBoundingClientRect());
        }
        fetchPanel(url).then(setPanelHtml);
    }

    function openOverlay(card) {
        if (elevatedCard) {
            closeOverlay();
        }

        var rect = card.getBoundingClientRect();

        placeholder = document.createElement('div');
        placeholder.style.height = rect.height + 'px';
        placeholder.style.marginBottom = '10px';
        card.parentNode.insertBefore(placeholder, card);

        card.style.position = 'fixed';
        card.style.top = rect.top + 'px';
        card.style.left = rect.left + 'px';
        card.style.width = rect.width + 'px';
        card.classList.add('elevated');
        elevatedCard = card;

        createSmallPanelShell(rect);

        var boardId = card.dataset.boardId;
        var cardId = card.dataset.cardId;
        fetchPanel('/Board/CardOptionsPanel?boardId=' + boardId + '&cardId=' + cardId).then(setPanelHtml);
    }

    function onKeyDown(ev) {
        if (ev.key === 'Escape') {
            closeOverlay();
        }
    }

    function onDetailKeyDown(ev) {
        if (ev.key === 'Escape' && !panelContainer) {
            closeDetailModal();
        }
    }

    function closeDetailModal() {
        if (detailBackdrop && detailBackdrop.parentNode) {
            detailBackdrop.parentNode.removeChild(detailBackdrop);
        }
        detailBackdrop = null;
        detailModalEl = null;
        pendingCardChanges = null;
        document.removeEventListener('keydown', onDetailKeyDown);
    }

    // Kartı Aç penceresi acikken eklenti ekleme/kaldirma islemleri sunucuya
    // HEMEN gonderilmez; sadece bu nesnede biriktirilir ve pencerenin Kaydet
    // butonuna basildiginda toplu olarak uygulanir (Iptal Et, pencereyi
    // sunucudan yeniden getirerek hepsini atar). Etiketler artik karta ozgu
    // dogrudan CRUD islemleri oldugundan (assign/unassign yok) hemen
    // uygulanir, staged degildir.
    function openCardDetailModal(boardId, cardId) {
        closeOverlay();
        if (detailBackdrop) {
            closeDetailModal();
        }

        pendingCardChanges = {
            newLinkAttachments: [],
            newFileAttachments: [],
            removedAttachmentIds: []
        };

        detailBackdrop = document.createElement('div');
        detailBackdrop.className = 'board-card-detail-backdrop';
        detailModalEl = document.createElement('div');
        detailModalEl.className = 'board-card-detail-modal';
        detailBackdrop.appendChild(detailModalEl);
        document.body.appendChild(detailBackdrop);

        fetchPanel('/Board/CardDetail?boardId=' + boardId + '&cardId=' + cardId).then(function (html) {
            detailModalEl.innerHTML = html;
            if (window.BoardRichText) {
                window.BoardRichText.init(detailModalEl);
            }
        });

        detailBackdrop.addEventListener('click', function (ev) {
            if (ev.target === detailBackdrop) {
                closeDetailModal();
            }
        });
        document.addEventListener('keydown', onDetailKeyDown);
    }

    function closeOverlay() {
        if (elevatedCard) {
            elevatedCard.style.position = '';
            elevatedCard.style.top = '';
            elevatedCard.style.left = '';
            elevatedCard.style.width = '';
            elevatedCard.classList.remove('elevated');
            elevatedCard = null;
        }
        if (placeholder && placeholder.parentNode) {
            placeholder.parentNode.removeChild(placeholder);
        }
        placeholder = null;
        if (backdrop && backdrop.parentNode) {
            backdrop.parentNode.removeChild(backdrop);
        }
        backdrop = null;
        if (panelContainer && panelContainer.parentNode) {
            panelContainer.parentNode.removeChild(panelContainer);
        }
        panelContainer = null;
        panelAnchorRect = null;
        document.removeEventListener('keydown', onKeyDown);
        window.removeEventListener('resize', positionPanel);
    }

    // assigned=true: strip yoksa olusturur, varsa ad/rengini gunceller (etiket
    // duzenleme artik dogrudan bu kartin kendi etiketini degistirdigi icin guvenli).
    // assigned=false: strip'i kaldirir (etiket silindi).
    function applyLabelStripToWrap(wrap, labelId, assigned, name, color) {
        var existing = wrap.querySelector('.board-card-label-strip[data-label-id="' + labelId + '"]');
        if (assigned) {
            if (existing) {
                existing.textContent = name;
                existing.style.background = color;
            } else {
                var strip = document.createElement('span');
                strip.className = 'board-card-label-strip';
                strip.dataset.labelId = labelId;
                strip.style.background = color;
                strip.textContent = name;
                wrap.appendChild(strip);
            }
        } else if (existing) {
            existing.remove();
        }
        wrap.style.display = wrap.children.length ? '' : 'none';
    }

    function updateCardLabelStrip(cardId, labelId, assigned, name, color) {
        var card = document.querySelector('.board-card[data-card-id="' + cardId + '"]');
        if (card) {
            var wrap = card.querySelector('.board-card-labels');
            if (!wrap) {
                wrap = document.createElement('div');
                wrap.className = 'board-card-labels';
                card.insertBefore(wrap, card.querySelector('.board-card-title'));
            }
            applyLabelStripToWrap(wrap, labelId, assigned, name, color);
        }

        if (detailModalEl) {
            var inner = detailModalEl.querySelector('.board-card-detail-modal-inner[data-card-id="' + cardId + '"]');
            var detailWrap = inner ? inner.querySelector('.board-card-detail-labels-list') : null;
            if (detailWrap) {
                applyLabelStripToWrap(detailWrap, labelId, assigned, name, color);
            }
        }
    }

    var ATTACHMENT_FILE_ICON = '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/>';
    var ATTACHMENT_LINK_ICON = '<path d="M10 13a5 5 0 0 0 7.07 0l2.83-2.83a5 5 0 0 0-7.07-7.07L11.5 4.5"/><path d="M14 11a5 5 0 0 0-7.07 0l-2.83 2.83a5 5 0 0 0 7.07 7.07L12.5 19.5"/>';
    var REACTION_EMOJIS = ['👍', '❤️', '😂', '😮', '😢', '🙏'];

    function appendAttachmentToModal(cardId, attachment) {
        if (!detailModalEl) {
            return;
        }
        var list = detailModalEl.querySelector('.board-card-detail-attachments-list[data-card-id="' + cardId + '"]');
        if (!list) {
            return;
        }

        var item = document.createElement('div');
        item.className = 'board-attachment-item' + (attachment.pending ? ' is-pending' : '');
        item.dataset.attachmentId = attachment.id;
        var icon = '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round">' +
            (attachment.isFile ? ATTACHMENT_FILE_ICON : ATTACHMENT_LINK_ICON) + '</svg>';

        if (attachment.pending && attachment.isFile) {
            // Dosya henuz yuklenmedi (Kaydet'te yuklenecek); tiklanabilir gercek
            // bir bagliantisi olmadigi icin <a> yerine <span> kullanilir.
            item.innerHTML = '<span class="board-attachment-link board-attachment-link-pending">' + icon + '<span></span></span>' +
                '<button type="button" class="board-attachment-remove-btn" data-attachment-id="' + attachment.id + '" title="Kaldır">&times;</button>';
        } else {
            item.innerHTML = '<a href="' + attachment.url + '" target="_blank" rel="noopener noreferrer" class="board-attachment-link">' + icon + '<span></span></a>' +
                '<button type="button" class="board-attachment-remove-btn" data-attachment-id="' + attachment.id + '" title="Kaldır">&times;</button>';
        }
        item.querySelector('span:last-child').textContent = attachment.displayName;
        item.querySelector('.board-attachment-remove-btn').dataset.boardId = list.dataset.boardId;
        item.querySelector('.board-attachment-remove-btn').dataset.cardId = cardId;
        list.insertBefore(item, list.firstChild);
    }

    function prependCommentToModal(boardId, cardId, comment) {
        if (!detailModalEl) {
            return;
        }
        var inner = detailModalEl.querySelector('.board-card-detail-modal-inner[data-card-id="' + cardId + '"]');
        var list = inner ? inner.querySelector('.board-comments-list') : null;
        if (!list) {
            return;
        }

        var item = document.createElement('div');
        item.className = 'board-comment-item';
        item.dataset.commentId = comment.id;
        item.innerHTML =
            '<div class="board-comment-meta">' +
            '<span class="board-comment-author"></span>' +
            '<span class="status-badge status-badge-unassigned"></span>' +
            '<span class="board-comment-date"></span>' +
            (comment.canEdit ? '<button type="button" class="board-comment-edit-btn" data-comment-id="' + comment.id + '" data-board-id="' + boardId + '" data-card-id="' + cardId + '" title="Yorumu düzenle">' +
                '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"><path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z"/></svg></button>' : '') +
            '</div>' +
            '<div class="board-comment-body"></div>' +
            '<div class="board-comment-footer">' +
            '<div class="board-comment-reactions"></div>' +
            '<button type="button" class="board-comment-react-btn" data-comment-id="' + comment.id + '" title="Tepki ver">' +
            '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.75" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"/><path d="M8 14s1.5 2 4 2 4-2 4-2"/><line x1="9" y1="9" x2="9.01" y2="9"/><line x1="15" y1="9" x2="15.01" y2="9"/></svg></button>' +
            '<div class="board-reaction-picker" data-comment-id="' + comment.id + '" data-board-id="' + boardId + '" data-card-id="' + cardId + '" style="display:none;">' +
            REACTION_EMOJIS.map(function (e) { return '<button type="button" class="board-reaction-picker-emoji" data-emoji="' + e + '">' + e + '</button>'; }).join('') +
            '</div>' +
            '</div>';
        item.querySelector('.board-comment-author').textContent = comment.displayName;
        item.querySelector('.status-badge').textContent = comment.role;
        item.querySelector('.board-comment-date').textContent = comment.createdAt;
        item.querySelector('.board-comment-body').dataset.original = comment.body;
        item.querySelector('.board-comment-body').innerHTML = comment.body;
        list.insertBefore(item, list.firstChild);
    }

    function renderCommentReactions(commentItem, reactions, currentUserId, boardId, cardId, commentId) {
        var wrap = commentItem.querySelector('.board-comment-reactions');
        if (!wrap) {
            return;
        }
        var byEmoji = {};
        reactions.forEach(function (r) {
            if (!byEmoji[r.emoji]) {
                byEmoji[r.emoji] = [];
            }
            byEmoji[r.emoji].push(r);
        });

        wrap.innerHTML = '';
        Object.keys(byEmoji).forEach(function (emoji) {
            var users = byEmoji[emoji];
            var mine = users.some(function (u) { return u.userId === currentUserId; });
            var chip = document.createElement('button');
            chip.type = 'button';
            chip.className = 'board-comment-reaction-chip' + (mine ? ' mine' : '');
            chip.dataset.commentId = commentId;
            chip.dataset.boardId = boardId;
            chip.dataset.cardId = cardId;
            chip.dataset.emoji = emoji;
            chip.innerHTML = '<span>' + emoji + '</span><span class="board-comment-reaction-count">' + users.length + '</span>';
            wrap.appendChild(chip);
        });
    }

    function startEditingComment(commentItem, boardId, cardId, commentId) {
        if (commentItem.querySelector('.board-comment-edit-rte')) {
            return;
        }
        var bodyEl = commentItem.querySelector('.board-comment-body');
        var referenceToolbar = document.querySelector('.board-rte-toolbar');
        if (!referenceToolbar) {
            return;
        }

        var wrapper = document.createElement('div');
        wrapper.className = 'board-rte board-comment-edit-rte';
        wrapper.setAttribute('data-rte', '');
        wrapper.appendChild(referenceToolbar.cloneNode(true));

        var content = document.createElement('div');
        content.className = 'board-rte-content';
        content.contentEditable = 'true';
        content.innerHTML = bodyEl.dataset.original || bodyEl.innerHTML;
        wrapper.appendChild(content);

        var actions = document.createElement('div');
        actions.className = 'board-card-detail-desc-actions board-comment-edit-actions';
        actions.innerHTML =
            '<button type="button" class="btn-chip btn-chip-primary board-comment-edit-save-btn">Kaydet</button>' +
            '<button type="button" class="btn-chip board-comment-edit-cancel-btn">İptal</button>';

        bodyEl.style.display = 'none';
        bodyEl.insertAdjacentElement('afterend', actions);
        bodyEl.insertAdjacentElement('afterend', wrapper);

        if (window.BoardRichText) {
            window.BoardRichText.init(wrapper);
        }
        content.focus();

        function finishEditing() {
            wrapper.remove();
            actions.remove();
            bodyEl.style.display = '';
        }

        actions.querySelector('.board-comment-edit-save-btn').addEventListener('click', function () {
            postForm('/Board/UpdateCardComment', {
                boardId: boardId, cardId: cardId, commentId: commentId, body: content.innerHTML
            }).then(function (r) {
                return r.ok ? r.json() : null;
            }).then(function (data) {
                if (!data) {
                    return;
                }
                bodyEl.dataset.original = data.body;
                bodyEl.innerHTML = data.body;
                finishEditing();
            });
        });
        actions.querySelector('.board-comment-edit-cancel-btn').addEventListener('click', finishEditing);
    }

    // Kartı Aç penceresindeki Kaydet butonu: aciklama + bekleyen eklenti
    // ekleme/kaldirma islemlerinin hepsini tek seferde sunucuya uygular,
    // basariliysa sayfa tazelenir (boylece pano listesindeki kart da guncel
    // haliyle gorunur). Etiketler artik hemen uygulandigi icin (staged degil)
    // burada islenmez.
    function commitCardDetailChanges(boardId, cardId, triggerBtn) {
        if (triggerBtn) {
            triggerBtn.disabled = true;
        }

        var requests = [];

        var descContent = detailModalEl.querySelector('.board-desc-content');
        requests.push(postForm('/Board/UpdateCardDescription', {
            boardId: boardId,
            cardId: cardId,
            description: descContent.innerHTML
        }));

        if (pendingCardChanges) {
            pendingCardChanges.removedAttachmentIds.forEach(function (attachmentId) {
                requests.push(postForm('/Board/DeleteCardAttachment', { boardId: boardId, cardId: cardId, attachmentId: attachmentId }));
            });

            pendingCardChanges.newLinkAttachments.forEach(function (entry) {
                requests.push(postForm('/Board/AddCardAttachmentLink', { boardId: boardId, cardId: cardId, url: entry.url }));
            });

            pendingCardChanges.newFileAttachments.forEach(function (entry) {
                requests.push(postFile('/Board/UploadCardAttachmentFile', { boardId: boardId, cardId: cardId }, entry.file));
            });
        }

        Promise.all(requests).then(function () {
            window.location.reload();
        }).catch(function () {
            if (triggerBtn) {
                triggerBtn.disabled = false;
            }
            UiDialog.toast({ message: 'Kaydedilirken bir hata oluştu, lütfen tekrar deneyin.', tone: 'error' });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        // Delege dinleyici kullanilir cunku jenerik sablonlarda onay/red/tasima
        // sonrasi kart DOM'u AJAX ile (applyCardFragmentUpdate) tamamen yeni bir
        // elemanla degistiriliyor; sayfa yuklendiginde tek tek baglanan bir
        // listener bu yeni elemanda bulunmadigindan "Kartı Aç" (duzenle) ikonu
        // o karti bir daha tiklanamaz hale geliyordu.
        document.body.addEventListener('click', function (ev) {
            var editBtn = ev.target.closest('.board-card-edit-btn');
            if (editBtn) {
                ev.stopPropagation();
                openOverlay(editBtn.closest('.board-card'));
            }
        });

        // Panel icerigi sunucudan defalarca degistigi (options -> etiketler ->
        // duzenleyici) icin tum etkilesimler tek bir delege listener uzerinden
        // yonetiliyor; her fetch sonrasi yeniden baglama gerekmiyor.
        document.body.addEventListener('click', function (ev) {
            if (!panelContainer || !panelContainer.contains(ev.target)) {
                return;
            }

            var closeBtn = ev.target.closest('.board-card-menu-close-btn');
            if (closeBtn) {
                closeOverlay();
                return;
            }

            var openBtn = ev.target.closest('[data-action="open"]');
            if (openBtn) {
                openCardDetailModal(openBtn.dataset.boardId, openBtn.dataset.cardId);
                return;
            }

            var editLabelsBtn = ev.target.closest('[data-action="edit-labels"]');
            if (editLabelsBtn) {
                var boardId = editLabelsBtn.dataset.boardId;
                var cardId = editLabelsBtn.dataset.cardId;
                fetchPanel('/Board/CardLabelsPanel?boardId=' + boardId + '&cardId=' + cardId).then(setPanelHtml);
                return;
            }

            var changeCoverBtn = ev.target.closest('[data-action="change-cover"]');
            if (changeCoverBtn) {
                fetchPanel('/Board/CardCoverPanel?boardId=' + changeCoverBtn.dataset.boardId + '&cardId=' + changeCoverBtn.dataset.cardId).then(setPanelHtml);
                return;
            }

            var archiveBtn = ev.target.closest('[data-action="archive"]');
            if (archiveBtn) {
                postForm('/Board/ArchiveCard', {
                    boardId: archiveBtn.dataset.boardId,
                    cardId: archiveBtn.dataset.cardId
                }).then(function (r) {
                    if (r.ok) {
                        closeOverlay();
                        window.location.reload();
                    }
                });
                return;
            }

            // Sadece kartin bulundugu listede kart ekleme yetkisi olan roller
            // icin aktif (bkz. BoardController.CanDeleteCardAsync); yetkisiz
            // listelerde buton "disabled" geldigi icin buraya hic girilmez.
            var deleteBtn = ev.target.closest('[data-action="delete"]');
            if (deleteBtn) {
                if (deleteBtn.disabled) {
                    return;
                }

                UiDialog.confirm({
                    title: 'Kartı sil',
                    message: 'Bu kart, yorumları ve ekleriyle birlikte kalıcı olarak silinecek. Bu işlem geri alınamaz.',
                    confirmText: 'Sil',
                    tone: 'danger'
                }).then(function (ok) {
                    if (!ok) {
                        return;
                    }
                    postForm('/Board/DeleteCard', {
                        boardId: deleteBtn.dataset.boardId,
                        cardId: deleteBtn.dataset.cardId
                    }).then(function (r) {
                        if (r.ok) {
                            closeOverlay();
                            window.location.reload();
                        }
                    });
                });
                return;
            }

            // Etiket duzenleyicideki geri (caret) ikonu: etiket listesine doner.
            var backBtn = ev.target.closest('.board-card-menu-back-btn');
            if (backBtn) {
                var backBoardId = backBtn.dataset.boardId;
                var backCardId = backBtn.dataset.cardId;
                fetchPanel('/Board/CardLabelsPanel?boardId=' + backBoardId + '&cardId=' + backCardId).then(setPanelHtml);
                return;
            }

            var editPencil = ev.target.closest('.board-label-edit-btn');
            if (editPencil) {
                var pBoardId = editPencil.dataset.boardId;
                var pCardId = editPencil.dataset.cardId;
                var labelId = editPencil.dataset.labelId;
                fetchPanel('/Board/LabelEditorPanel?boardId=' + pBoardId + '&cardId=' + pCardId + '&labelId=' + labelId).then(setPanelHtml);
                return;
            }

            var createBtn = ev.target.closest('.board-label-create-btn');
            if (createBtn) {
                var cBoardId = createBtn.dataset.boardId;
                var cCardId = createBtn.dataset.cardId;
                fetchPanel('/Board/LabelEditorPanel?boardId=' + cBoardId + '&cardId=' + cCardId).then(setPanelHtml);
                return;
            }

            var colorSwatch = ev.target.closest('.board-label-color-swatch');
            if (colorSwatch) {
                var form = colorSwatch.closest('form');
                form.querySelectorAll('.board-label-color-swatch').forEach(function (s) {
                    s.classList.toggle('selected', s === colorSwatch);
                });
                form.querySelector('.board-label-color-input').value = colorSwatch.dataset.color;
                return;
            }

            var deleteBtn = ev.target.closest('.board-label-delete-btn');
            if (deleteBtn) {
                var delBoardId = deleteBtn.dataset.boardId;
                var delCardId = deleteBtn.dataset.cardId;
                var delLabelId = deleteBtn.dataset.labelId;
                UiDialog.confirm({
                    title: 'Etiketi sil',
                    message: 'Etiket panodan tamamen kaldırılacak ve eklendiği tüm kartlardan silinecek.',
                    confirmText: 'Sil',
                    tone: 'danger'
                }).then(function (ok) {
                    if (!ok) {
                        return;
                    }
                    postForm('/Board/DeleteLabel', { boardId: delBoardId, cardId: delCardId, labelId: delLabelId })
                        .then(function (r) {
                            if (!r.ok) {
                                return;
                            }
                            updateCardLabelStrip(delCardId, delLabelId, false, '', '');
                            fetchPanel('/Board/CardLabelsPanel?boardId=' + delBoardId + '&cardId=' + delCardId).then(setPanelHtml);
                        });
                });
                return;
            }

            var coverColorSwatch = ev.target.closest('.board-cover-color-swatch');
            if (coverColorSwatch) {
                postForm('/Board/SetCardCoverColor', {
                    boardId: coverColorSwatch.dataset.boardId,
                    cardId: coverColorSwatch.dataset.cardId,
                    color: coverColorSwatch.dataset.color
                }).then(function (r) { return r.json(); }).then(function (data) {
                    applyCardCover(data);
                    fetchPanel('/Board/CardCoverPanel?boardId=' + coverColorSwatch.dataset.boardId + '&cardId=' + coverColorSwatch.dataset.cardId).then(setPanelHtml);
                });
                return;
            }

            var coverPresetThumb = ev.target.closest('.board-cover-preset-thumb');
            if (coverPresetThumb) {
                postForm('/Board/SetCardCoverPreset', {
                    boardId: coverPresetThumb.dataset.boardId,
                    cardId: coverPresetThumb.dataset.cardId,
                    presetKey: coverPresetThumb.dataset.presetKey
                }).then(function (r) { return r.json(); }).then(function (data) {
                    applyCardCover(data);
                    fetchPanel('/Board/CardCoverPanel?boardId=' + coverPresetThumb.dataset.boardId + '&cardId=' + coverPresetThumb.dataset.cardId).then(setPanelHtml);
                });
                return;
            }

            var coverRemoveBtn = ev.target.closest('.board-cover-remove-btn');
            if (coverRemoveBtn) {
                postForm('/Board/ClearCardCover', {
                    boardId: coverRemoveBtn.dataset.boardId,
                    cardId: coverRemoveBtn.dataset.cardId
                }).then(function (r) { return r.json(); }).then(function (data) {
                    applyCardCover(data);
                    fetchPanel('/Board/CardCoverPanel?boardId=' + coverRemoveBtn.dataset.boardId + '&cardId=' + coverRemoveBtn.dataset.cardId).then(setPanelHtml);
                });
                return;
            }

            var attachmentUploadBtn = ev.target.closest('.board-attachment-upload-btn');
            if (attachmentUploadBtn) {
                var attFileInputToOpen = panelContainer.querySelector('.board-attachment-file-input');
                if (attFileInputToOpen) {
                    attFileInputToOpen.click();
                }
                return;
            }

            var coverUploadBtn = ev.target.closest('.board-cover-upload-btn');
            if (coverUploadBtn) {
                var fileInput = panelContainer.querySelector('.board-cover-file-input');
                if (fileInput) {
                    fileInput.click();
                }
                return;
            }

        });

        document.body.addEventListener('change', function (ev) {
            if (!panelContainer || !panelContainer.contains(ev.target)) {
                return;
            }

            // Etiket secim kutucugu: isaretliyse etiket kartta gorunur, degilse
            // gizlenir. Etiket silinmez; listede kalmaya devam eder.
            if (ev.target.classList.contains('board-label-checkbox')) {
                var checkbox = ev.target;
                var lblBoardId = checkbox.dataset.boardId;
                var lblCardId = checkbox.dataset.cardId;
                var lblId = checkbox.dataset.labelId;
                var isSelected = checkbox.checked;

                postForm('/Board/ToggleLabel', {
                    boardId: lblBoardId,
                    cardId: lblCardId,
                    labelId: lblId,
                    selected: isSelected
                })
                    .then(function (r) { return r.ok ? r.json() : null; })
                    .then(function (data) {
                        if (!data) {
                            // Basarisizsa kutucugu eski haline al.
                            checkbox.checked = !isSelected;
                            return;
                        }
                        updateCardLabelStrip(lblCardId, data.labelId, data.selected, data.name, data.color);
                    })
                    .catch(function () {
                        checkbox.checked = !isSelected;
                    });
                return;
            }

            if (ev.target.classList.contains('board-cover-file-input')) {
                var fileInput = ev.target;
                var file = fileInput.files && fileInput.files[0];
                if (!file) {
                    return;
                }
                var uploadBoardId = fileInput.dataset.boardId;
                var uploadCardId = fileInput.dataset.cardId;
                postFile('/Board/UploadCardCover', { boardId: uploadBoardId, cardId: uploadCardId }, file)
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        applyCardCover(data);
                        fetchPanel('/Board/CardCoverPanel?boardId=' + uploadBoardId + '&cardId=' + uploadCardId).then(setPanelHtml);
                    });
            }

            if (ev.target.classList.contains('board-attachment-file-input')) {
                var attFileInput = ev.target;
                var attFile = attFileInput.files && attFileInput.files[0];
                if (!attFile) {
                    return;
                }

                if (detailModalEl && pendingCardChanges) {
                    var fileTempId = 'pending-file-' + (++pendingIdCounter);
                    pendingCardChanges.newFileAttachments.push({ tempId: fileTempId, file: attFile });
                    appendAttachmentToModal(attFileInput.dataset.cardId, {
                        id: fileTempId, isFile: true, url: '#', displayName: attFile.name, pending: true
                    });
                    attFileInput.value = '';
                    return;
                }

                postFile('/Board/UploadCardAttachmentFile', {
                    boardId: attFileInput.dataset.boardId,
                    cardId: attFileInput.dataset.cardId
                }, attFile)
                    .then(function (r) { return r.json(); })
                    .then(function (data) {
                        appendAttachmentToModal(attFileInput.dataset.cardId, data);
                    });
            }
        });

        document.body.addEventListener('input', function (ev) {
            if (!panelContainer || !panelContainer.contains(ev.target)) {
                return;
            }

            if (ev.target.classList.contains('board-label-search')) {
                var term = ev.target.value.trim().toLowerCase();
                panelContainer.querySelectorAll('.board-label-row').forEach(function (row) {
                    var name = row.dataset.labelName || '';
                    row.style.display = name.indexOf(term) === -1 ? 'none' : '';
                });
            }
        });

        document.body.addEventListener('submit', function (ev) {
            if (!panelContainer || !panelContainer.contains(ev.target)) {
                return;
            }

            if (ev.target.classList.contains('board-label-editor-form')) {
                ev.preventDefault();
                var form = ev.target;
                var boardId = form.dataset.boardId;
                var cardId = form.dataset.cardId;
                var labelId = form.dataset.labelId;
                var name = form.querySelector('.board-label-name-input').value;
                var color = form.querySelector('.board-label-color-input').value;

                var fields = { boardId: boardId, cardId: cardId, name: name, color: color };
                if (labelId) {
                    fields.labelId = labelId;
                }

                postForm('/Board/SaveLabel', fields).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
                    if (!data) {
                        return;
                    }
                    // Kartta gorunurluk secim kutucuguna baglidir; duzenleme tek
                    // basina etiketi karta eklemez.
                    updateCardLabelStrip(cardId, data.labelId, data.selected, data.name, data.color);
                    fetchPanel('/Board/CardLabelsPanel?boardId=' + boardId + '&cardId=' + cardId).then(setPanelHtml);
                });
            }

            if (ev.target.classList.contains('board-attachment-link-form')) {
                ev.preventDefault();
                var linkForm = ev.target;
                var urlInput = linkForm.querySelector('.board-attachment-url-input');
                var rawUrl = urlInput.value.trim();
                if (!rawUrl) {
                    return;
                }

                if (detailModalEl && pendingCardChanges) {
                    var normalizedUrl;
                    try {
                        normalizedUrl = new URL(rawUrl).toString();
                    } catch (e) {
                        UiDialog.toast({ message: 'Geçerli bir bağlantı adresi girin.', tone: 'warning' });
                        return;
                    }
                    var linkTempId = 'pending-link-' + (++pendingIdCounter);
                    pendingCardChanges.newLinkAttachments.push({ tempId: linkTempId, url: normalizedUrl });
                    appendAttachmentToModal(linkForm.dataset.cardId, {
                        id: linkTempId, isFile: false, url: normalizedUrl, displayName: normalizedUrl, pending: true
                    });
                    urlInput.value = '';
                    return;
                }

                postForm('/Board/AddCardAttachmentLink', {
                    boardId: linkForm.dataset.boardId,
                    cardId: linkForm.dataset.cardId,
                    url: rawUrl
                }).then(function (r) {
                    if (!r.ok) {
                        return null;
                    }
                    return r.json();
                }).then(function (data) {
                    if (!data) {
                        return;
                    }
                    appendAttachmentToModal(linkForm.dataset.cardId, data);
                    urlInput.value = '';
                });
            }
        });

        // Kart detay penceresinin (Kartı Aç) icindeki tetikleyiciler icin ayri bir
        // delege listener: bu buton/elemanlar panelContainer'in DEGIL, buyuk
        // pencerenin (detailModalEl) icinde oldugu icin yukaridaki listener'in
        // guard'ina takilmazlar.
        document.body.addEventListener('click', function (ev) {
            if (!detailModalEl || !detailModalEl.contains(ev.target)) {
                return;
            }

            if (!ev.target.closest('.board-comment-react-btn') && !ev.target.closest('.board-reaction-picker')) {
                detailModalEl.querySelectorAll('.board-reaction-picker').forEach(function (p) { p.style.display = 'none'; });
            }

            var detailCloseBtn = ev.target.closest('.board-card-detail-close-btn');
            if (detailCloseBtn) {
                closeDetailModal();
                return;
            }

            var changeCoverIcon = ev.target.closest('[data-action="detail-change-cover"]');
            if (changeCoverIcon) {
                showSmallPanel(changeCoverIcon, '/Board/CardCoverPanel?boardId=' + changeCoverIcon.dataset.boardId + '&cardId=' + changeCoverIcon.dataset.cardId);
                return;
            }

            // Kart detayindaki arsivle ikonu: ara menu yok, dogrudan arsivler.
            var detailArchiveIcon = ev.target.closest('[data-action="detail-archive"]');
            if (detailArchiveIcon) {
                postForm('/Board/ArchiveCard', {
                    boardId: detailArchiveIcon.dataset.boardId,
                    cardId: detailArchiveIcon.dataset.cardId
                }).then(function (r) {
                    if (r.ok) {
                        closeDetailModal();
                        window.location.reload();
                    }
                });
                return;
            }

            var editLabelsRow = ev.target.closest('[data-action="detail-edit-labels"]');
            if (editLabelsRow) {
                showSmallPanel(editLabelsRow, '/Board/CardLabelsPanel?boardId=' + editLabelsRow.dataset.boardId + '&cardId=' + editLabelsRow.dataset.cardId);
                return;
            }

            var addAttachmentRow = ev.target.closest('[data-action="detail-add-attachment"]');
            if (addAttachmentRow) {
                showSmallPanel(addAttachmentRow, '/Board/CardAttachmentPanel?boardId=' + addAttachmentRow.dataset.boardId + '&cardId=' + addAttachmentRow.dataset.cardId);
                return;
            }

            var removeAttachmentBtn = ev.target.closest('.board-attachment-remove-btn');
            if (removeAttachmentBtn) {
                // Eklenti listesi sadece Kartı Aç penceresinde gorunur; kaldirma
                // da Kaydet'e kadar sunucuya gonderilmez, sadece biriktirilir.
                var attId = removeAttachmentBtn.dataset.attachmentId;
                if (pendingCardChanges) {
                    if (String(attId).indexOf('pending-') === 0) {
                        pendingCardChanges.newLinkAttachments = pendingCardChanges.newLinkAttachments.filter(function (a) { return a.tempId !== attId; });
                        pendingCardChanges.newFileAttachments = pendingCardChanges.newFileAttachments.filter(function (a) { return a.tempId !== attId; });
                    } else {
                        pendingCardChanges.removedAttachmentIds.push(attId);
                    }
                }
                removeAttachmentBtn.closest('.board-attachment-item').remove();
                return;
            }

            var descSaveBtn = ev.target.closest('.board-desc-save-btn');
            if (descSaveBtn) {
                commitCardDetailChanges(descSaveBtn.dataset.boardId, descSaveBtn.dataset.cardId, descSaveBtn);
                return;
            }

            var descCancelBtn = ev.target.closest('.board-desc-cancel-btn');
            if (descCancelBtn) {
                // Hicbir sey sunucuya gonderilmedigi icin iptal, pencereyi
                // sunucudan oldugu gibi yeniden getirmek demektir; boylece
                // aciklama, etiket ve eklenti taslaklarinin hepsi birden atilir.
                var cancelBoardId = detailModalEl.querySelector('.board-card-detail-modal-inner').dataset.boardId;
                var cancelCardId = detailModalEl.querySelector('.board-card-detail-modal-inner').dataset.cardId;
                openCardDetailModal(cancelBoardId, cancelCardId);
                return;
            }

            var commentPlaceholder = ev.target.closest('.board-comment-placeholder');
            if (commentPlaceholder) {
                var composer = commentPlaceholder.closest('.board-comment-composer');
                commentPlaceholder.style.display = 'none';
                var rte = composer.querySelector('.board-comment-rte');
                rte.style.display = '';
                var saveBtn = composer.querySelector('.board-comment-save-btn');
                saveBtn.style.display = '';
                composer.querySelector('.board-comment-content').focus();
                return;
            }

            var commentSaveBtn = ev.target.closest('.board-comment-save-btn');
            if (commentSaveBtn && !commentSaveBtn.disabled) {
                var commentComposer = commentSaveBtn.closest('.board-comment-composer');
                var commentContent = commentComposer.querySelector('.board-comment-content');
                postForm('/Board/AddCardComment', {
                    boardId: commentComposer.dataset.boardId,
                    cardId: commentComposer.dataset.cardId,
                    body: commentContent.innerHTML
                }).then(function (r) {
                    if (!r.ok) {
                        return null;
                    }
                    return r.json();
                }).then(function (data) {
                    if (!data) {
                        return;
                    }
                    prependCommentToModal(commentComposer.dataset.boardId, commentComposer.dataset.cardId, data);
                    commentContent.innerHTML = '';
                    commentComposer.querySelector('.board-comment-rte').style.display = 'none';
                    commentSaveBtn.style.display = 'none';
                    commentSaveBtn.disabled = true;
                    commentComposer.querySelector('.board-comment-placeholder').style.display = '';
                });
                return;
            }

            var commentEditBtn = ev.target.closest('.board-comment-edit-btn');
            if (commentEditBtn) {
                startEditingComment(
                    commentEditBtn.closest('.board-comment-item'),
                    commentEditBtn.dataset.boardId,
                    commentEditBtn.dataset.cardId,
                    commentEditBtn.dataset.commentId
                );
                return;
            }

            var reactBtn = ev.target.closest('.board-comment-react-btn');
            if (reactBtn) {
                var picker = reactBtn.parentElement.querySelector('.board-reaction-picker');
                var wasOpen = picker.style.display !== 'none';
                detailModalEl.querySelectorAll('.board-reaction-picker').forEach(function (p) { p.style.display = 'none'; });
                picker.style.display = wasOpen ? 'none' : 'flex';
                return;
            }

            var pickerEmojiBtn = ev.target.closest('.board-reaction-picker-emoji');
            if (pickerEmojiBtn) {
                var picker2 = pickerEmojiBtn.closest('.board-reaction-picker');
                picker2.style.display = 'none';
                postForm('/Board/ToggleCommentReaction', {
                    boardId: picker2.dataset.boardId,
                    cardId: picker2.dataset.cardId,
                    commentId: picker2.dataset.commentId,
                    emoji: pickerEmojiBtn.dataset.emoji
                }).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
                    if (!data) {
                        return;
                    }
                    var commentItem = detailModalEl.querySelector('.board-comment-item[data-comment-id="' + data.commentId + '"]');
                    if (commentItem) {
                        renderCommentReactions(commentItem, data.reactions, data.currentUserId, picker2.dataset.boardId, picker2.dataset.cardId, data.commentId);
                    }
                });
                return;
            }

            var reactionChip = ev.target.closest('.board-comment-reaction-chip');
            if (reactionChip) {
                postForm('/Board/ToggleCommentReaction', {
                    boardId: reactionChip.dataset.boardId,
                    cardId: reactionChip.dataset.cardId,
                    commentId: reactionChip.dataset.commentId,
                    emoji: reactionChip.dataset.emoji
                }).then(function (r) { return r.ok ? r.json() : null; }).then(function (data) {
                    if (!data) {
                        return;
                    }
                    var commentItem = detailModalEl.querySelector('.board-comment-item[data-comment-id="' + data.commentId + '"]');
                    if (commentItem) {
                        renderCommentReactions(commentItem, data.reactions, data.currentUserId, reactionChip.dataset.boardId, reactionChip.dataset.cardId, data.commentId);
                    }
                });
                return;
            }
        });

        document.body.addEventListener('input', function (ev) {
            if (!detailModalEl || !detailModalEl.contains(ev.target)) {
                return;
            }

            if (ev.target.classList.contains('board-comment-content')) {
                var composer = ev.target.closest('.board-comment-composer');
                var saveBtn = composer.querySelector('.board-comment-save-btn');
                var text = ev.target.textContent.replace(/\s+/g, '');
                saveBtn.disabled = text.length === 0;
            }
        });
    });
})();
