// The signature on the Proof of delivery form: drawn on a canvas, posted as a PNG data URL in a hidden field. Nothing is posted until the Driver has drawn.
(function () {
    function setup(canvas) {
        const form = canvas.closest('form');
        const field = form.querySelector('[data-signature-field]');
        const clear = form.querySelector('[data-signature-clear]');
        const context = canvas.getContext('2d');
        let drawing = false;
        let drawn = false;

        context.lineWidth = 3;
        context.lineCap = 'round';
        context.strokeStyle = '#1c1c1c';

        function at(event) {
            const box = canvas.getBoundingClientRect();
            return { x: (event.clientX - box.left) * (canvas.width / box.width), y: (event.clientY - box.top) * (canvas.height / box.height) };
        }

        canvas.addEventListener('pointerdown', function (event) {
            drawing = true;
            canvas.setPointerCapture(event.pointerId);
            const point = at(event);
            context.beginPath();
            context.moveTo(point.x, point.y);
            context.lineTo(point.x + 0.01, point.y);
            context.stroke();
            drawn = true;
        });
        canvas.addEventListener('pointermove', function (event) {
            if (!drawing) { return; }
            const point = at(event);
            context.lineTo(point.x, point.y);
            context.stroke();
        });
        const stop = function () { drawing = false; };
        canvas.addEventListener('pointerup', stop);
        canvas.addEventListener('pointercancel', stop);

        clear.addEventListener('click', function () {
            context.clearRect(0, 0, canvas.width, canvas.height);
            drawn = false;
            field.value = '';
        });
        form.addEventListener('submit', function () {
            field.value = drawn ? canvas.toDataURL('image/png') : '';
        });
        canvas.dataset.ready = 'true';
    }

    function init() {
        document.querySelectorAll('canvas[data-signature]:not([data-ready])').forEach(setup);
    }

    init();
    // A page reached by enhanced navigation is not loaded again, so its script tags don't run: look for new canvases when it has loaded.
    if (window.Blazor) {
        window.Blazor.addEventListener('enhancedload', init);
    }
}());
