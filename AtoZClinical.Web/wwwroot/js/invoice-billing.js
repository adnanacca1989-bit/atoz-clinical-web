document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#invoicePatientNameInput',
        fieldMap: standardPatientFieldMap(true)
    });
});
