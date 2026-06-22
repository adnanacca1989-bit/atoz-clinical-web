document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#cashReceiptPatientNameInput',
        fieldMap: standardPatientFieldMap(true)
    });
    bindWrittenAmount('[name="Input.Amount"]', '[name="Input.WrittenAmount"]');
});
