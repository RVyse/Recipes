window.medappFullCalendar = (function () {
    let calendar = null;

    function ensureFullCalendar() {
        if (!window.FullCalendar) throw 'FullCalendar not loaded. Include FullCalendar scripts in index.html.';
    }

    function init(elementId, events, dotNetRef) {
        ensureFullCalendar();
        const el = document.getElementById(elementId);
        if (!el) return;

        if (calendar) {
            calendar.destroy();
            calendar = null;
        }

        calendar = new FullCalendar.Calendar(el, {
            initialView: 'dayGridMonth',
            height: 600,
            events: events || [],
            eventClick: function (info) {
                if (dotNetRef) dotNetRef.invokeMethodAsync('NotifyEventClicked', info.event.id);
            }
        });

        calendar.render();
    }

    function updateEvents(events) {
        if (!calendar) return;
        calendar.removeAllEvents();
        if (events && events.length) calendar.addEventSource(events);
    }

    function getWindowWidth() {
        return window.innerWidth || document.documentElement.clientWidth || document.body.clientWidth;
    }

    function moveModalToBody(id) {
        try {
            const el = document.getElementById(id);
            if (!el) return;
            if (el.parentElement !== document.body) document.body.appendChild(el);
        }
        catch (e) {
            // ignore
        }
    }

    function showModal(id) {
        try {
            const el = document.getElementById(id);
            if (!el) return;
            moveModalToBody(id);
            if (window.bootstrap && window.bootstrap.Modal) {
                // reuse instance if present to avoid duplicate backdrops
                let inst = window.bootstrap.Modal.getInstance(el);
                if (inst) inst.show();
                else {
                    inst = new window.bootstrap.Modal(el);
                    inst.show();
                }
            } else {
                // fallback: toggle class
                el.classList.add('show');
                el.style.display = 'block';
            }
        }
        catch (e) { }
    }

    function hideModal(id) {
        try {
            const el = document.getElementById(id);
            if (!el) return;
            if (window.bootstrap && window.bootstrap.Modal) {
                const m = window.bootstrap.Modal.getInstance(el);
                if (m) m.hide();
            } else {
                el.classList.remove('show');
                el.style.display = 'none';
            }
        }
        catch (e) { }
    }

    return {
        init: init,
        updateEvents: updateEvents,
        getWindowWidth: getWindowWidth,
        moveModalToBody: moveModalToBody,
        showModal: showModal,
        hideModal: hideModal
    };
})();
