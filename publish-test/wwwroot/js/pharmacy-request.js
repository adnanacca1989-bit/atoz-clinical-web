document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#pharmacyRequestPatientNameInput',
        fieldMap: standardPatientFieldMap(true)
    });
    initPharmacyLineCalculations({
        grandTotalId: 'pharmacyRequestTotal',
        qtySelector: '.pharmacy-qty',
        rateSelector: '.pharmacy-price'
    });
});
