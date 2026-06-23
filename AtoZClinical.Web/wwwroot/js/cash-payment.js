document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#cashPaymentPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPatientCashBalance
    });
    initPatientBarcodeScanner({
        barcodeSelector: '#cashPaymentPatientBarcodeInput',
        patientNameSelector: '#cashPaymentPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPatientCashBalance
    });
    bindWrittenAmount('[name="Input.Amount"]', '[name="Input.WrittenAmount"]');
    bindCashBalanceUpdates();
    refreshCashBalanceIfPatientSelected();
});
