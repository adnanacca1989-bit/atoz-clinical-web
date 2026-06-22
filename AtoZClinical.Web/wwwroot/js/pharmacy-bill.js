document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#pharmacyBillPatientNameInput',
        fieldMap: standardPatientFieldMap(true)
    });
    initPharmacyLineCalculations({
        qtySelector: '.pharmacy-qty',
        rateSelector: '.pharmacy-price',
        subTotalId: 'pharmacyBillSubTotal',
        discountId: 'pharmacyBillDiscount',
        netId: 'pharmacyBillNet',
        paidId: 'pharmacyBillPaid',
        balanceId: 'pharmacyBillBalance'
    });
});
