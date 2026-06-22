document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#cashPaymentPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPatientCashBalance
    });
    bindWrittenAmount('[name="Input.Amount"]', '[name="Input.WrittenAmount"]');
    bindCashBalanceUpdates();
    refreshCashBalanceIfPatientSelected();
});
