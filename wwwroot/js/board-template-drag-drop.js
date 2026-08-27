// Klasik disi pano sablonlari icin jenerik surukle-birak. board-drag-drop.js
// ile ayni FLIP animasyonlu, sayfa yenilemeyen mekanizmayi kullanir; tek
// fark hangi kaynak->hedef liste geciskerinin izinli oldugunun sabit rol
// isimleri yerine sunucudan gelen data-transitions JSON'undan (mevcut
// kullanicinin roluyle onceden filtrelenmis) okunmasidir. Klasik'in
// board-drag-drop.js'i bu elemanlara hic dokunmaz (data-drag-role="generic"
// degeri oradaki isAllowedDrop'ta hicbir zaman eslenmez), bu dosya da
// data-drag-role="generic" olmayan kartlara hic dokunmaz.
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

    function parseTransitions(container) {
        try {
            return JSON.parse((container && container.dataset.transitions) || '[]');
        } catch (e) {
            return [];
        }
    }

    function findTransition(transitions, fromKey, toKey) {
        return transitions.find(function (t) {
            return (t.from === '*' || t.from === fromKey) && t.to === toKey;
        }) || null;
    }

    function isAllowedDrop(card, column) {
        if (!card || card.dataset.dragRole !== 'generic') {
            return false;
        }
        var container = column.closest('.board-columns--scrollable');
        var transitions = parseTransitions(container);
        var fromKey = card.dataset.listKey;
        var toKey = column.dataset.listKey;
        if (fromKey === toKey) {
            return false;
        }
        return !!findTransition(transitions, fromKey, toKey);
    }

    function capturePositions(body) {
        var map = new Map();
        Array.prototype.forEach.call(body.children, function (child) {
            map.set(child, child.getBoundingClientRect());
        });
        return map;
    }

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

    var AUTOSCROLL_EDGE = 70;
    var AUTOSCROLL_MAX_SPEED = 16;
    var lastPointer = { x: 0, y: 0 };
    var autoScrollRafId = null;

    function onDragTick(ev) {
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

        var scrollableColumns = elUnderPointer && elUnderPointer.closest ? elUnderPointer.closest('.board-columns--scrollable') : null;
        if (scrollableColumns) {
            var colsRect = scrollableColumns.getBoundingClientRect();
            var leftDist = x - colsRect.left;
            var rightDist = colsRect.right - x;
            if (leftDist >= 0 && leftDist < AUTOSCROLL_EDGE) {
                scrollableColumns.scrollLeft -= edgeSpeed(leftDist);
            } else if (rightDist >= 0 && rightDist < AUTOSCROLL_EDGE) {
                scrollableColumns.scrollLeft += edgeSpeed(rightDist);
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

    // Parametreler icin bkz. board-drag-drop.js'teki ayni isimli fonksiyon:
    // aciklama artik yerlestirilmis bir pencerede sorulduğu icin geri alma,
    // 'dragend' modul degiskenlerini temizledikten sonra calisabilir.
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
        document.querySelectorAll('.board-columns--scrollable .board-column.drag-over').forEach(function (c) {
            c.classList.remove('drag-over');
        });

        var container = column.closest('.board-columns--scrollable');
        var transitions = parseTransitions(container);
        var fromKey = draggedCard.dataset.listKey;
        var targetListKey = column.dataset.listKey;
        var boardId = draggedCard.dataset.boardId;
        var cardId = draggedCard.dataset.cardId;
        var cardEl = draggedCard;
        var slot = placeholder;
        var slotBody = body;
        var sourceBody = originalBody;

        var transition = findTransition(transitions, fromKey, targetListKey);

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

        if (!transition) {
            finalizeFailure();
            return;
        }

        function submitMove(note) {
            postForm('/Board/MoveCard', {
                boardId: boardId,
                cardId: cardId,
                targetListKey: targetListKey,
                targetPosition: 1,
                note: note || ''
            }).then(function (r) {
                r.ok ? finalizeSuccess() : finalizeFailure();
            }).catch(finalizeFailure);
        }

        if (!transition.requiresNote) {
            submitMove('');
            return;
        }

        var titleEl = column.querySelector('.board-column-title');
        var targetLabel = titleEl ? titleEl.textContent.trim() : '';

        UiDialog.prompt({
            title: 'Taşıma gerekçesi',
            message: targetLabel
                ? ('Kart "' + targetLabel + '" listesine taşınacak. Bu taşıma için bir açıklama gerekiyor.')
                : 'Bu taşıma için bir açıklama gerekiyor.',
            label: 'Açıklama / gerekçe',
            placeholder: 'Örn. Kod incelemesi tamamlandı, teste hazır.',
            multiline: true,
            required: true,
            requiredMessage: 'Bu taşıma için bir açıklama girmelisiniz.',
            confirmText: 'Taşı',
            tone: 'primary'
        }).then(function (note) {
            if (note === null) {
                finalizeFailure();
                return;
            }
            submitMove(note);
        });
    }

    function initBoardDragDrop() {
        var columnsWrap = document.querySelector('.board-columns--scrollable');
        if (!columnsWrap) {
            return;
        }

        document.addEventListener('dragstart', function (ev) {
            var card = ev.target && ev.target.closest ? ev.target.closest('.board-card[data-drag-role="generic"]') : null;
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

            document.querySelectorAll('.board-columns--scrollable .board-column.drag-over').forEach(function (c) {
                c.classList.remove('drag-over');
            });

            if (!moveHandled) {
                revertDrag();
            }

            draggedCard = null;
        });

        document.querySelectorAll('.board-columns--scrollable .board-column').forEach(function (column) {
            var body = column.querySelector('.board-column-body');

            column.addEventListener('dragover', function (ev) {
                if (!draggedCard || !isAllowedDrop(draggedCard, column)) {
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
                if (!draggedCard || !isAllowedDrop(draggedCard, column)) {
                    return;
                }
                ev.preventDefault();
                handleDrop(column, body);
            });
        });
    }

    // "Tasi: X" / "Onayla" / "Reddet" gibi buton tabanli aksiyonlar da (surukle-
    // birak ile ayni sekilde) sayfa yenilemeden, kart parcasini yeniden cekip
    // yerine yerlestirerek calisir. Bu formlar surukleme kullanmadigi icin
    // placeholder/FLIP yok; kart doğrudan hedef listenin sonuna tasinir (backend
    // zaten gecisleri hep listenin sonuna ekliyor, bu yuzden bu tutarlidir).
    function applyCardFragmentUpdate(cardEl, boardId, cardId) {
        return fetchCardFragment(boardId, cardId).then(function (html) {
            var wrapper = document.createElement('div');
            wrapper.innerHTML = html.trim();
            var newCardEl = wrapper.firstElementChild;
            if (!newCardEl) {
                window.location.reload();
                return;
            }

            var oldBody = cardEl.closest('.board-column-body');
            var newListKey = newCardEl.dataset.listKey;
            var targetColumn = document.querySelector('.board-columns--scrollable .board-column[data-list-key="' + newListKey + '"]');
            var targetBody = targetColumn ? targetColumn.querySelector('.board-column-body') : oldBody;

            newCardEl.classList.add('board-card-settling');

            if (targetBody === oldBody) {
                oldBody.replaceChild(newCardEl, cardEl);
            } else {
                var anchor = targetBody.querySelector(':scope > .board-add-card-btn');
                if (anchor) {
                    targetBody.insertBefore(newCardEl, anchor);
                } else {
                    targetBody.appendChild(newCardEl);
                }
                cardEl.remove();
            }

            requestAnimationFrame(function () {
                requestAnimationFrame(function () {
                    newCardEl.classList.remove('board-card-settling');
                });
            });

            refreshColumnChrome(oldBody.closest('.board-column'));
            if (targetBody !== oldBody) {
                refreshColumnChrome(targetBody.closest('.board-column'));
            }
        }).catch(function () {
            window.location.reload();
        });
    }

    function initGenericFormActions() {
        document.addEventListener('submit', function (ev) {
            var form = ev.target;
            if (!form || !form.classList || !form.classList.contains('js-generic-move-form')) {
                return;
            }

            var cardEl = form.closest('.board-card');
            if (!cardEl) {
                return;
            }

            ev.preventDefault();

            var boardId = cardEl.dataset.boardId;
            var cardId = cardEl.dataset.cardId;
            var body = new FormData(form);

            fetch(form.action, {
                method: 'POST',
                headers: { 'X-Requested-With': 'XMLHttpRequest' },
                body: body
            }).then(function (r) {
                if (!r.ok) {
                    return r.text().then(function (msg) {
                        UiDialog.toast({ message: msg || 'İşlem gerçekleştirilemedi.', tone: 'error' });
                    });
                }

                var openDetails = form.closest('details');
                if (openDetails) {
                    openDetails.open = false;
                }

                return applyCardFragmentUpdate(cardEl, boardId, cardId);
            }).catch(function () {
                window.location.reload();
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        initBoardDragDrop();
        initGenericFormActions();
    });
})();
