// Pano surukle-birak: mühendis Yapilacaklar->Test, musteri Test->Yapilacaklar (red)
// veya Test->Tamamlanan (onay) icin kart tasima. Tam sayfa yenileme yerine
// AJAX + DOM manipulasyonu kullanir; boylece (1) surukleme sirasinda hedef
// listedeki kartlar FLIP animasyonuyla yer acar, (2) birakildiginda kart
// sunucudan (yeni baglamina uygun butonlarla) tekrar cizilip yumusakca
// yerine "oturur", tek bir "once yok / sonra var" sayfa yenileme yanip
// sonmesi olmadan.
(function () {
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
            headers: {
                'Content-Type': 'application/x-www-form-urlencoded',
                'X-Requested-With': 'XMLHttpRequest'
            },
            body: body.toString()
        });
    }

    function fetchCardFragment(boardId, cardId) {
        var url = '/Board/CardFragment?boardId=' + encodeURIComponent(boardId) + '&cardId=' + encodeURIComponent(cardId);
        return fetch(url, { headers: { 'X-Requested-With': 'XMLHttpRequest' } }).then(function (r) {
            if (!r.ok) {
                throw new Error('card-fragment-fetch-failed');
            }
            return r.text();
        });
    }

    function isAllowedDrop(role, targetListKey) {
        if (role === 'engineer-move-test') {
            return targetListKey === 'test';
        }
        if (role === 'customer-test-card') {
            return targetListKey === 'todo' || targetListKey === 'done';
        }
        return false;
    }

    function capturePositions(body) {
        var map = new Map();
        Array.prototype.forEach.call(body.children, function (child) {
            map.set(child, child.getBoundingClientRect());
        });
        return map;
    }

    // FLIP: konum degisen her cocuk icin (eski - yeni) farkini anlik olarak
    // transform ile "geri" uygulayip, bir sonraki karede transition'la 0'a
    // dondurur; boylece tarayici yeniden duzeni anlik atlamak yerine kayarak gosterir.
    function playFlip(body, firstRects) {
        Array.prototype.forEach.call(body.children, function (child) {
            var first = firstRects.get(child);
            if (!first) {
                return;
            }
            var last = child.getBoundingClientRect();
            var deltaY = first.top - last.top;
            if (Math.abs(deltaY) > 0.5) {
                child.style.transition = 'none';
                child.style.transform = 'translateY(' + deltaY + 'px)';
                void child.offsetHeight;
                requestAnimationFrame(function () {
                    child.style.transition = 'transform 0.2s ease';
                    child.style.transform = '';
                });
            }
        });
    }

    function removeEmptyNote(body) {
        var note = body.querySelector(':scope > .board-empty-note');
        if (note) {
            note.remove();
        }
    }

    function getAddCardAnchor(body) {
        return body.querySelector(':scope > .board-add-card-btn');
    }

    function refreshColumnChrome(column) {
        if (!column) {
            return;
        }
        var body = column.querySelector('.board-column-body');
        var countEl = column.querySelector('.board-column-count');
        var cardCount = body.querySelectorAll(':scope > .board-card').length;
        if (countEl) {
            countEl.textContent = String(cardCount);
        }
        if (cardCount === 0) {
            if (!body.querySelector(':scope > .board-empty-note')) {
                var note = document.createElement('p');
                note.className = 'board-empty-note';
                note.textContent = 'Bu kolonda madde yok.';
                var anchor = getAddCardAnchor(body);
                if (anchor) {
                    body.insertBefore(note, anchor);
                } else {
                    body.appendChild(note);
                }
            }
        } else {
            removeEmptyNote(body);
        }
    }

    function computeInsertBeforeNode(body, clientY) {
        var cards = Array.prototype.filter.call(body.children, function (el) {
            return el.classList.contains('board-card');
        });
        for (var i = 0; i < cards.length; i++) {
            var rect = cards[i].getBoundingClientRect();
            if (clientY < rect.top + rect.height / 2) {
                return cards[i];
            }
        }
        return getAddCardAnchor(body) || null;
    }

    var draggedCard = null;
    var placeholder = null;
    var placeholderBody = null;
    var originalBody = null;
    var moveHandled = false;

    // Surukleme sirasinda imlec ekranin ust/alt kenarina (ya da kaydirilabilir
    // bir kolon govdesinin kendi ust/alt kenarina) yaklasirsa otomatik olarak
    // yukari/asagi kaydirir; boylece uzun bir listenin altindan baslayan
    // surukleme, ekran disinda kalan hedef kolona da tasinabilir.
    var AUTOSCROLL_EDGE = 70;
    var AUTOSCROLL_MAX_SPEED = 16;
    var lastPointer = { x: 0, y: 0 };
    var autoScrollRafId = null;

    function onDragTick(ev) {
        // Bazi tarayicilarda surukleme bittiginde gonderilen son 'drag'
        // olayinda clientX/clientY (0,0) gelir; bu sahte konumu yok sayiyoruz.
        if (ev.clientX === 0 && ev.clientY === 0) {
            return;
        }
        lastPointer.x = ev.clientX;
        lastPointer.y = ev.clientY;
    }

    function edgeSpeed(distance) {
        var ratio = Math.min(1, Math.max(0, 1 - (distance / AUTOSCROLL_EDGE)));
        return Math.ceil(ratio * AUTOSCROLL_MAX_SPEED);
    }

    function autoScrollStep() {
        if (!draggedCard) {
            autoScrollRafId = null;
            return;
        }

        var x = lastPointer.x;
        var y = lastPointer.y;

        var elUnderPointer = document.elementFromPoint(x, y);
        var scrollableBody = elUnderPointer && elUnderPointer.closest ? elUnderPointer.closest('.board-column-body') : null;
        if (scrollableBody && scrollableBody.scrollHeight > scrollableBody.clientHeight) {
            var rect = scrollableBody.getBoundingClientRect();
            var topDist = y - rect.top;
            var bottomDist = rect.bottom - y;
            if (topDist >= 0 && topDist < AUTOSCROLL_EDGE) {
                scrollableBody.scrollTop -= edgeSpeed(topDist);
            } else if (bottomDist >= 0 && bottomDist < AUTOSCROLL_EDGE) {
                scrollableBody.scrollTop += edgeSpeed(bottomDist);
            }
        }

        if (y < AUTOSCROLL_EDGE) {
            window.scrollBy(0, -edgeSpeed(y));
        } else if (window.innerHeight - y < AUTOSCROLL_EDGE) {
            window.scrollBy(0, edgeSpeed(window.innerHeight - y));
        }

        autoScrollRafId = requestAnimationFrame(autoScrollStep);
    }

    function startAutoScroll() {
        if (autoScrollRafId === null) {
            autoScrollRafId = requestAnimationFrame(autoScrollStep);
        }
    }

    function stopAutoScroll() {
        if (autoScrollRafId !== null) {
            cancelAnimationFrame(autoScrollRafId);
            autoScrollRafId = null;
        }
    }

    function createPlaceholder(rect) {
        var el = document.createElement('div');
        el.className = 'board-card-placeholder';
        el.style.height = rect.height + 'px';
        return el;
    }

    function movePlaceholder(newBody, clientY) {
        var beforeNode = computeInsertBeforeNode(newBody, clientY);
        if (placeholderBody === newBody && placeholder.nextElementSibling === beforeNode) {
            return;
        }

        var bodies = (placeholderBody && placeholderBody !== newBody) ? [placeholderBody, newBody] : [newBody];
        var firstRectsList = bodies.map(capturePositions);

        removeEmptyNote(newBody);
        if (beforeNode) {
            newBody.insertBefore(placeholder, beforeNode);
        } else {
            newBody.appendChild(placeholder);
        }
        placeholderBody = newBody;

        bodies.forEach(function (body, idx) {
            playFlip(body, firstRectsList[idx]);
        });

        if (bodies.length > 1) {
            refreshColumnChrome(bodies[0].closest('.board-column'));
        }
    }

    // card/srcBody/slot acikca verilebilir cunku red gerekcesi artik yerlestirilmis
    // bir pencerede (UiDialog.prompt) soruluyor; pencere kapandiginda 'dragend'
    // coktan calismis ve modul duzeyindeki draggedCard/placeholder temizlenmis
    // olur. Bu durumda geri alma, handleDrop'ta yakalanan referanslarla yapilir.
    function revertDrag(card, srcBody, slot) {
        var slotEl = slot || placeholder;
        if (slotEl && slotEl.parentElement) {
            var body = slotEl.parentElement;
            var firstRects = capturePositions(body);
            slotEl.remove();
            playFlip(body, firstRects);
            refreshColumnChrome(body.closest('.board-column'));
        }
        placeholder = null;
        placeholderBody = null;

        var cardEl = card || draggedCard;
        if (cardEl) {
            var sourceBody = srcBody || originalBody;
            var firstRects2 = sourceBody ? capturePositions(sourceBody) : null;
            cardEl.style.display = '';
            cardEl.classList.remove('dragging');
            if (sourceBody) {
                playFlip(sourceBody, firstRects2);
                refreshColumnChrome(sourceBody.closest('.board-column'));
            }
        }
    }

    function handleDrop(column, body) {
        moveHandled = true;
        document.querySelectorAll('.board-column.drag-over').forEach(function (c) {
            c.classList.remove('drag-over');
        });

        var role = draggedCard.dataset.dragRole;
        var targetListKey = column.dataset.listKey;
        var boardId = draggedCard.dataset.boardId;
        var cardId = draggedCard.dataset.cardId;
        var cardEl = draggedCard;
        var slot = placeholder;
        var slotBody = body;
        var sourceBody = originalBody;

        function finalizeFailure() {
            moveHandled = false;
            revertDrag(cardEl, sourceBody, slot);
        }

        function finalizeSuccess() {
            fetchCardFragment(boardId, cardId).then(function (html) {
                var wrapper = document.createElement('div');
                wrapper.innerHTML = html.trim();
                var newCardEl = wrapper.firstElementChild;
                if (!newCardEl || !slot.parentElement) {
                    window.location.reload();
                    return;
                }

                newCardEl.classList.add('board-card-settling');
                slot.parentElement.replaceChild(newCardEl, slot);
                cardEl.remove();

                requestAnimationFrame(function () {
                    requestAnimationFrame(function () {
                        newCardEl.classList.remove('board-card-settling');
                    });
                });

                refreshColumnChrome(slotBody.closest('.board-column'));
                refreshColumnChrome(sourceBody && sourceBody.closest('.board-column'));

                placeholder = null;
                placeholderBody = null;
            }).catch(function () {
                window.location.reload();
            });
        }

        if (role === 'engineer-move-test' && targetListKey === 'test') {
            postForm('/Board/MoveToTest', { cardId: cardId, boardId: boardId })
                .then(function (r) { r.ok ? finalizeSuccess() : finalizeFailure(); })
                .catch(finalizeFailure);
        } else if (role === 'customer-test-card' && targetListKey === 'done') {
            postForm('/Board/Approve', { cardId: cardId, boardId: boardId })
                .then(function (r) { r.ok ? finalizeSuccess() : finalizeFailure(); })
                .catch(finalizeFailure);
        } else if (role === 'customer-test-card' && targetListKey === 'todo') {
            UiDialog.prompt({
                title: 'Kartı reddet',
                message: 'Kart "Yapılacaklar" listesine geri gönderilecek. Mühendisin ne yapması gerektiğini kısaca yazın.',
                label: 'Reddetme sebebi',
                placeholder: 'Örn. Test ortamında hata devam ediyor.',
                multiline: true,
                required: true,
                requiredMessage: 'Reddetmek için bir açıklama girmelisiniz.',
                confirmText: 'Reddet',
                tone: 'warning'
            }).then(function (note) {
                if (note === null) {
                    finalizeFailure();
                    return;
                }
                postForm('/Board/Reject', { cardId: cardId, boardId: boardId, Note: note })
                    .then(function (r) { r.ok ? finalizeSuccess() : finalizeFailure(); })
                    .catch(finalizeFailure);
            });
        } else {
            finalizeFailure();
        }
    }

    function initBoardDragDrop() {
        var columnsWrap = document.querySelector('.board-columns');
        if (!columnsWrap) {
            return;
        }

        document.addEventListener('dragstart', function (ev) {
            var card = ev.target && ev.target.closest ? ev.target.closest('.board-card[draggable="true"]') : null;
            if (!card) {
                return;
            }

            draggedCard = card;
            moveHandled = false;
            originalBody = card.parentElement;
            placeholder = createPlaceholder(card.getBoundingClientRect());
            placeholderBody = null;

            lastPointer.x = ev.clientX;
            lastPointer.y = ev.clientY;
            card.addEventListener('drag', onDragTick);
            startAutoScroll();

            ev.dataTransfer.effectAllowed = 'move';
            ev.dataTransfer.setData('text/plain', card.dataset.cardId || '');

            // Kartin surukleme "hayaleti" tarayici tarafindan dragstart anindaki
            // gorunumden anlik olarak alinir; gizlemeyi bir sonraki tur'a
            // erteleyerek hayalet goruntusunun bozulmasini onluyoruz.
            setTimeout(function () {
                if (!draggedCard || !originalBody) {
                    return;
                }
                var firstRects = capturePositions(originalBody);
                draggedCard.classList.add('dragging');
                draggedCard.style.display = 'none';
                playFlip(originalBody, firstRects);
                refreshColumnChrome(originalBody.closest('.board-column'));
            }, 0);
        });

        document.addEventListener('dragend', function (ev) {
            var card = ev.target && ev.target.closest ? ev.target.closest('.board-card') : null;
            if (!card || card !== draggedCard) {
                return;
            }

            card.removeEventListener('drag', onDragTick);
            stopAutoScroll();

            document.querySelectorAll('.board-column.drag-over').forEach(function (c) {
                c.classList.remove('drag-over');
            });

            if (!moveHandled) {
                revertDrag();
            }

            draggedCard = null;
        });

        document.querySelectorAll('.board-column').forEach(function (column) {
            var body = column.querySelector('.board-column-body');

            column.addEventListener('dragover', function (ev) {
                if (!draggedCard) {
                    return;
                }
                var role = draggedCard.dataset.dragRole;
                if (!isAllowedDrop(role, column.dataset.listKey)) {
                    return;
                }
                ev.preventDefault();
                ev.dataTransfer.dropEffect = 'move';
                column.classList.add('drag-over');
                movePlaceholder(body, ev.clientY);
            });

            column.addEventListener('dragleave', function (ev) {
                if (ev.target === column && (!ev.relatedTarget || !column.contains(ev.relatedTarget))) {
                    column.classList.remove('drag-over');
                }
            });

            column.addEventListener('drop', function (ev) {
                if (!draggedCard) {
                    return;
                }
                var role = draggedCard.dataset.dragRole;
                if (!isAllowedDrop(role, column.dataset.listKey)) {
                    return;
                }
                ev.preventDefault();
                handleDrop(column, body);
            });
        });
    }

    document.addEventListener('DOMContentLoaded', initBoardDragDrop);
})();
