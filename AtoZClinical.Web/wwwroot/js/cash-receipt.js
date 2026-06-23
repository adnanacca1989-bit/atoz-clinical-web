document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#cashReceiptPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPatientCashBalance
    });
    initPatientBarcodeScanner({
        barcodeSelector: '#cashReceiptPatientBarcodeInput',
        patientNameSelector: '#cashReceiptPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPatientCashBalance
    });
    bindWrittenAmount('[name="Input.Amount"]', '[name="Input.WrittenAmount"]');
    bindCashBalanceUpdates();
    refreshCashBalanceIfPatientSelected();
});