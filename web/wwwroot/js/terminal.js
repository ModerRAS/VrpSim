(function () {
    'use strict';

    var engine = null;
    var prompt = '';
    var buffer = '';
    var history = [];
    var histIdx = -1;
    var exited = false;
    var outEl, promptEl, bufEl, keyEl, lineEl;

    function addLine(text) {
        var d = document.createElement('div');
        d.className = 'term-line';
        d.textContent = text === '' ? ' ' : text;
        outEl.appendChild(d);
        outEl.scrollTop = outEl.scrollHeight;
    }

    function addLines(lines) {
        if (!lines) return;
        for (var i = 0; i < lines.length; i++) addLine(lines[i]);
    }

    function redraw() {
        promptEl.textContent = prompt;
        bufEl.textContent = buffer;
        outEl.scrollTop = outEl.scrollHeight;
    }

    function focusKey() {
        try { keyEl.focus({ preventScroll: true }); } catch (e) { keyEl.focus(); }
    }

    async function submit() {
        if (exited) return;
        var line = buffer;
        buffer = '';
        histIdx = -1;
        addLine(prompt + line);
        redraw();
        if (line.trim().length === 0) return;
        history.push(line);
        var res = await engine.invokeMethodAsync('Submit', line);
        addLines(res.lines);
        prompt = res.prompt || '';
        if (res.exit) {
            exited = true;
            lineEl.style.display = 'none';
        } else {
            redraw();
        }
    }

    async function complete(help) {
        if (exited) return;
        var res = await engine.invokeMethodAsync('Complete', buffer, !!help);
        if (!help && res.hasInsert) {
            buffer = res.insert || '';
            redraw();
            return;
        }
        addLines(res.lines);
        redraw();
    }

    function onKeyDown(e) {
        if (e.ctrlKey && (e.key === 'c' || e.key === 'C')) {
            e.preventDefault();
            buffer = '';
            histIdx = -1;
            addLine(prompt + '^C');
            redraw();
            return;
        }
        if (e.key === 'Enter') {
            e.preventDefault();
            submit();
            return;
        }
        if (e.key === 'Tab') {
            e.preventDefault();
            complete(false);
            return;
        }
        if (e.key === '?') {
            e.preventDefault();
            complete(true);
            return;
        }
        if (e.key === 'Backspace') {
            e.preventDefault();
            if (buffer.length > 0) { buffer = buffer.slice(0, -1); redraw(); }
            return;
        }
        if (e.key === 'Escape') {
            e.preventDefault();
            buffer = '';
            histIdx = -1;
            redraw();
            return;
        }
        if (e.key === 'ArrowUp') {
            e.preventDefault();
            if (history.length === 0) return;
            if (histIdx === -1) histIdx = history.length - 1;
            else if (histIdx > 0) histIdx -= 1;
            buffer = history[histIdx];
            redraw();
            return;
        }
        if (e.key === 'ArrowDown') {
            e.preventDefault();
            if (histIdx === -1) return;
            if (histIdx < history.length - 1) histIdx += 1;
            else { histIdx = -1; buffer = ''; redraw(); return; }
            buffer = history[histIdx];
            redraw();
            return;
        }
        if (e.key.length === 1 && !e.ctrlKey && !e.altKey && !e.metaKey) {
            e.preventDefault();
            buffer += e.key;
            redraw();
        }
    }

    function onInput() {
        var v = keyEl.value;
        keyEl.value = '';
        if (!v) return;
        buffer += v;
        redraw();
    }

    async function start(dotNetRef) {
        engine = dotNetRef;
        outEl = document.getElementById('term-output');
        promptEl = document.getElementById('term-prompt');
        bufEl = document.getElementById('term-buffer');
        keyEl = document.getElementById('term-key');
        lineEl = document.getElementById('term-inputline');

        var res = await engine.invokeMethodAsync('Init');
        addLines(res.banner);
        prompt = res.prompt || '';
        lineEl.style.display = 'block';
        redraw();

        keyEl.addEventListener('keydown', onKeyDown);
        keyEl.addEventListener('input', onInput);
        var term = document.getElementById('term');
        term.addEventListener('click', focusKey);
        window.addEventListener('resize', function () { outEl.scrollTop = outEl.scrollHeight; });
        focusKey();
    }

    window.VrpTerm = { start: start };
})();
