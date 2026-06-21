document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#pharmacyRequestPatientNameInput',
        fieldMap: standardPatientFieldMap(true)
    });
});
