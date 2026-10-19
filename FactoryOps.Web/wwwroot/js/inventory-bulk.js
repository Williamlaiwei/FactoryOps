(() => {
    const form = document.getElementById('bulkStockForm');
    if (!form) return;
    const all = document.getElementById('selectAllStock');
    const selections = [...form.querySelectorAll('.stock-selection')];
    const count = document.getElementById('stockSelectionCount');
    function refresh() {
        const selected = selections.filter(x => x.checked).length;
        all.checked = selections.length > 0 && selected === selections.length;
        all.indeterminate = selected > 0 && selected < selections.length;
        if (count) count.textContent = `${selected} selected`;
    }
    all.addEventListener('change', () => { selections.forEach(x => x.checked = all.checked); refresh(); });
    selections.forEach(x => x.addEventListener('change', refresh));
    form.querySelectorAll('input[type="number"]').forEach(input => input.addEventListener('input', () => {
        input.closest('tr').querySelector('.stock-selection').checked = true;
        refresh();
    }));
    let saving = false;
    form.addEventListener('submit', event => {
        if (saving) { event.preventDefault(); return; }
        if (!selections.some(x => x.checked)) { event.preventDefault(); if (count) count.textContent = 'Select at least one product to save.'; return; }
        saving = true;
        document.getElementById('saveBulkStock').disabled = true;
    });
    window.addEventListener('pageshow', () => { saving = false; const button = document.getElementById('saveBulkStock'); if (button) button.disabled = false; });
    refresh();
})();
