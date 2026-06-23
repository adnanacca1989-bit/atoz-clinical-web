document.addEventListener('DOMContentLoaded', () => {
    const setLineField = (index, field, value) => {
        const el = document.querySelector(`[name="Lines[${index}].${field}"]`);
        if (el) el.value = value ?? '';
    };

    const clearBillLines = () => {
        for (let i = 0; i < 30; i++) {
            if (!document.querySelector(`[name="Lines[${i}].MedicineName"]`)) break;
            setLineField(i, 'LineNo', i + 1);
            ['Barcode', 'MedicineCode', 'MedicineName', 'Dosage', 'Uom', 'Qty', 'UnitPrice', 'Total'].forEach(f => setLineField(i, f, ''));
        }
    };

    const fillBillLines = (lines) => {
        clearBillLines();
        lines.forEach((line, i) => {
            setLineField(i, 'LineNo', line.lineNo ?? i + 1);
            setLineField(i, 'Barcode', line.barcode);
            setLineField(i, 'MedicineCode', line.medicineCode);
            setLineField(i, 'MedicineName', line.medicineName);
            setLineField(i, 'Dosage', line.dosage);
            setLineField(i, 'Uom', line.uom);
            setLineField(i, 'Qty', line.qty);
            setLineField(i, 'UnitPrice', line.unitPrice);
            setLineField(i, 'Total', line.total);
        });
        document.querySelector('.pharmacy-qty')?.dispatchEvent(new Event('input', { bubbles: true }));
    };

    const loadPharmacyRequest = async (patient) => {
        const params = new URLSearchParams();
        if (patient.patientNo) params.set('patientId', patient.patientNo);
        if (patient.name) params.set('patientName', patient.name);
        try {
            const res = await fetch(`/Pharmacy/RequestByPatient?${params}`);
            if (!res.ok) return;
            const data = await res.json();
            if (!data) return;
            const requestNoInput = document.querySelector('[name="Input.RequestNo"]');
            if (requestNoInput && data.requestNo) requestNoInput.value = data.requestNo;
            if (data.lines?.length) fillBillLines(data.lines);
        } catch { /* ignore */ }
    };

    initPatientPicker({
        patientNameSelector: '#pharmacyBillPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPharmacyRequest
    });

    initPatientBarcodeScanner({
        barcodeSelector: '#pharmacyBillPatientBarcodeInput',
        patientNameSelector: '#pharmacyBillPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: loadPharmacyRequest
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
