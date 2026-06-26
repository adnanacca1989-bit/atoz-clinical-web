document.addEventListener('DOMContentLoaded', () => {
    initPharmacyLineCalculations({
        grandTotalId: 'openingBalanceTotal',
        qtySelector: '.pharmacy-qty',
        rateSelector: '.pharmacy-cost'
    });
});
