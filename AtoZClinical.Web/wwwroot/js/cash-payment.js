document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#cashPaymentPatientNameInput',
        fieldMap: standardPatientFieldMap(true)
    });
});
