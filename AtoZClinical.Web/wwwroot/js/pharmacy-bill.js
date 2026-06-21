document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#pharmacyBillPatientNameInput',
        fieldMap: standardPatientFieldMap(true)
    });
});
