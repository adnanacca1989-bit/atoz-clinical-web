document.addEventListener('DOMContentLoaded', () => {
    const subTotalEl = document.getElementById('purchaseSubTotal');
    const discountPctEl = document.getElementById('discountPercent');
    const discountAmtEl = document.getElementById('discountAmount');
    const netEl = document.getElementById('netAmount');
    if (!subTotalEl || !discountPctEl || !discountAmtEl || !netEl) return;

    const calcSubTotal = () => {
        let sum = 0;
        document.querySelectorAll('.pharmacy-line').forEach(row => {
            const qty = parseFloat(row.querySelector('.purchase-qty')?.value || '0');
            const cost = parseFloat(row.querySelector('.purchase-cost')?.value || '0');
            const total = qty * cost;
            sum += total;
            const totalInput = row.querySelector('.purchase-line-total');
            if (totalInput) totalInput.value = total.toFixed(2);
        });
        subTotalEl.value = sum.toFixed(2);
        return sum;
    };

    const calcNet = (fromPercent) => {
        const sub = calcSubTotal();
        let disc = parseFloat(discountAmtEl.value || '0');
        const pct = parseFloat(discountPctEl.value || '0');
        if (fromPercent && pct > 0)
            disc = Math.round(sub * pct / 100 * 100) / 100;
        else if (!fromPercent && disc > 0 && sub > 0)
            discountPctEl.value = (Math.round(disc / sub * 10000) / 100).toFixed(2);
        else if (fromPercent && pct === 0)
            disc = parseFloat(discountAmtEl.value || '0');
        discountAmtEl.value = disc.toFixed(2);
        netEl.value = (sub - disc).toFixed(2);
    };

    discountPctEl.addEventListener('input', () => calcNet(true));
    discountAmtEl.addEventListener('input', () => calcNet(false));
    document.querySelectorAll('.purchase-qty, .purchase-cost').forEach(el => {
        el.addEventListener('input', () => calcNet(true));
    });
});
