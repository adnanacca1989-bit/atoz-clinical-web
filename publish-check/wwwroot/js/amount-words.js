function formatDateForInput(value) {
    if (!value) return '';
    const d = new Date(value);
    if (Number.isNaN(d.getTime())) return '';
    const y = d.getFullYear();
    const m = String(d.getMonth() + 1).padStart(2, '0');
    const day = String(d.getDate()).padStart(2, '0');
    return `${y}-${m}-${day}`;
}

function formatTimeForInput(value) {
    if (!value) return '';
    const d = new Date(`2000-01-01 ${value}`);
    if (Number.isNaN(d.getTime())) return '';
    return d.toTimeString().slice(0, 5);
}

function amountToWords(amount) {
    const n = Math.abs(Number(amount) || 0);
    if (n === 0) return 'Zero';
    const ones = ['Zero', 'One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven', 'Eight', 'Nine',
        'Ten', 'Eleven', 'Twelve', 'Thirteen', 'Fourteen', 'Fifteen', 'Sixteen', 'Seventeen', 'Eighteen', 'Nineteen'];
    const tens = ['', '', 'Twenty', 'Thirty', 'Forty', 'Fifty', 'Sixty', 'Seventy', 'Eighty', 'Ninety'];

    const convert = (num) => {
        if (num < 20) return ones[num];
        if (num < 100) return tens[Math.floor(num / 10)] + (num % 10 ? ' ' + ones[num % 10] : '');
        if (num < 1000) return ones[Math.floor(num / 100)] + ' Hundred' + (num % 100 ? ' ' + convert(num % 100) : '');
        if (num < 1000000) return convert(Math.floor(num / 1000)) + ' Thousand' + (num % 1000 ? ' ' + convert(num % 1000) : '');
        if (num < 1000000000) return convert(Math.floor(num / 1000000)) + ' Million' + (num % 1000000 ? ' ' + convert(num % 1000000) : '');
        return convert(Math.floor(num / 1000000000)) + ' Billion' + (num % 1000000000 ? ' ' + convert(num % 1000000000) : '');
    };

    const whole = Math.trunc(n);
    const cents = Math.round((n - whole) * 100);
    let words = (amount < 0 ? 'Negative ' : '') + convert(whole);
    if (cents > 0) words += ` and ${String(cents).padStart(2, '0')}/100`;
    return words;
}

function bindWrittenAmount(amountSelector, writtenSelector) {
    const amountEl = document.querySelector(amountSelector);
    const writtenEl = document.querySelector(writtenSelector);
    if (!amountEl || !writtenEl) return;
    const update = () => { writtenEl.value = amountToWords(amountEl.value); };
    amountEl.addEventListener('input', update);
    amountEl.addEventListener('change', update);
    if (amountEl.value) update();
}
