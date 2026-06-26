document.addEventListener('DOMContentLoaded', () => {

    const setLineField = (index, field, value) => {
        const el = document.querySelector(`[name="Lines[${index}].${field}"]`);
        if (el) el.value = value ?? '';
    };

    const syncItemSelect = (row, barcode, itemId, medicineName) => {
        if (!row) return;
        const select = row.querySelector('.pharmacy-item-select');
        if (!select) return;
        for (const opt of select.options) {
            if (itemId && opt.value === itemId) {
                select.value = opt.value;
                return;
            }
            if (barcode && (opt.dataset.barcode === barcode || opt.value === barcode)) {
                select.value = opt.value;
                return;
            }
            if (medicineName && opt.dataset.name?.toLowerCase() === medicineName.toLowerCase()) {
                select.value = opt.value;
                return;
            }
        }
    };

    const clearBillLines = () => {
        for (let i = 0; i < 30; i++) {
            if (!document.querySelector(`[name="Lines[${i}].MedicineName"]`)) break;
            setLineField(i, 'LineNo', i + 1);
            ['Barcode', 'MedicineCode', 'MedicineName', 'Dosage', 'Uom', 'Qty', 'UnitPrice'].forEach(f => setLineField(i, f, ''));
            const row = document.querySelector(`.pharmacy-line[data-line="${i}"]`);
            const totalEl = row?.querySelector('.pharmacy-line-total');
            if (totalEl) totalEl.value = '';
            syncItemSelect(row, '');
        }
    };

    const fillBillLines = (lines) => {
        if (!lines?.length) return;
        clearBillLines();
        lines.forEach((line, i) => {
            const price = line.defaultUnitPrice ?? line.unitPrice ?? 0;
            setLineField(i, 'LineNo', line.lineNo ?? i + 1);
            setLineField(i, 'Barcode', line.barcode);
            setLineField(i, 'MedicineCode', line.medicineCode);
            setLineField(i, 'MedicineName', line.medicineName);
            setLineField(i, 'Dosage', line.dosage);
            setLineField(i, 'Uom', line.uom);
            setLineField(i, 'Qty', line.qty);
            setLineField(i, 'UnitPrice', price);
            const row = document.querySelector(`.pharmacy-line[data-line="${i}"]`);
            syncItemSelect(row, line.barcode, line.itemId, line.medicineName);
            const totalEl = row?.querySelector('.pharmacy-line-total');
            if (totalEl) {
                const total = line.total ?? (Number(line.qty || 0) * Number(price || 0));
                totalEl.value = Number(total).toFixed(2);
            }
        });
        if (typeof window.recalcPharmacyLines === 'function') window.recalcPharmacyLines();
    };

    const applyRequestData = (data) => {
        if (!data) return;
        const setInput = (name, value) => {
            const el = document.querySelector(`[name="Input.${name}"]`);
            if (el && value != null && value !== '') el.value = value;
        };
        if (data.requestNo) setInput('RequestNo', data.requestNo);
        if (data.patientName) setInput('PatientName', data.patientName);
        if (data.patientId) setInput('PatientId', data.patientId);
        if (data.age != null) setInput('Age', data.age);
        if (data.gender) setInput('Gender', data.gender);
        if (data.phone) setInput('Phone', data.phone);
        if (data.city) setInput('City', data.city);
        if (data.doctorName) setInput('DoctorName', data.doctorName);
        if (data.specialty) setInput('Specialty', data.specialty);
        if (data.lines?.length) fillBillLines(data.lines);
    };

    const hasServerRenderedLines = () => {
        for (let i = 0; i < 8; i++) {
            const name = document.querySelector(`[name="Lines[${i}].MedicineName"]`)?.value?.trim();
            if (name) return true;
        }
        return false;
    };

    const loadPharmacyRequest = async (patient) => {
        const params = new URLSearchParams();
        const requestNoOnForm = document.querySelector('[name="Input.RequestNo"]')?.value?.trim();
        if (requestNoOnForm) params.set('requestNo', requestNoOnForm);
        if (patient?.patientNo) params.set('patientId', patient.patientNo);
        if (patient?.name) params.set('patientName', patient.name);
        if (!params.has('requestNo') && !params.has('patientId') && !params.has('patientName')) return;

        try {
            const res = await fetch(`/Pharmacy/RequestByPatient?${params}`, {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            });
            if (!res.ok) return;
            const data = await res.json();
            applyRequestData(data);
        } catch { /* ignore */ }
    };

    const loadPharmacyRequestByNo = async () => {
        const requestNo = document.querySelector('[name="Input.RequestNo"]')?.value?.trim();
        if (!requestNo) return;
        const params = new URLSearchParams({ requestNo });
        try {
            const res = await fetch(`/Pharmacy/RequestByPatient?${params}`, {
                credentials: 'same-origin',
                headers: { Accept: 'application/json' }
            });
            if (!res.ok) return;
            const data = await res.json();
            applyRequestData(data);
        } catch { /* ignore */ }
    };

    const onPatientSelected = async (patient) => {
        const recordId = document.querySelector('[name="RecordId"]')?.value?.trim();
        if (!recordId && patient?.patientNo) {
            window.location.href = `/Pharmacy/Bill?NewRecord=true&LoadPatientId=${encodeURIComponent(patient.patientNo)}`;
            return;
        }
        await loadPharmacyRequest(patient);
    };

    initPatientPicker({
        patientNameSelector: '#pharmacyBillPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: onPatientSelected
    });

    initPatientBarcodeScanner({
        barcodeSelector: '#pharmacyBillPatientBarcodeInput',
        patientNameSelector: '#pharmacyBillPatientNameInput',
        fieldMap: standardPatientFieldMap(true),
        onApply: onPatientSelected
    });

    document.querySelector('[name="Input.RequestNo"]')?.addEventListener('change', loadPharmacyRequestByNo);

    initPharmacyLineCalculations({
        qtySelector: '.pharmacy-qty',
        rateSelector: '.pharmacy-price',
        subTotalId: 'pharmacyBillSubTotal',
        discountId: 'pharmacyBillDiscount',
        netId: 'pharmacyBillNet',
        paidId: 'pharmacyBillPaid',
        balanceId: 'pharmacyBillBalance'
    });

    document.querySelectorAll('.pharmacy-line').forEach(row => {
        const select = row.querySelector('.pharmacy-item-select');
        if (!select) return;
        const syncHidden = () => {
            const opt = select.selectedOptions[0];
            if (!opt?.dataset?.name) return;
            const i = row.dataset.line;
            setLineField(i, 'Barcode', opt.dataset.barcode);
            setLineField(i, 'MedicineCode', opt.dataset.code);
            setLineField(i, 'MedicineName', opt.dataset.name);
            setLineField(i, 'Dosage', opt.dataset.dosage);
            if (!document.querySelector(`[name="Lines[${i}].Uom"]`)?.value)
                setLineField(i, 'Uom', opt.dataset.baseUom);
        };
        select.addEventListener('change', syncHidden);
    });

    const requestNo = document.querySelector('[name="Input.RequestNo"]')?.value?.trim();
    if (requestNo && !hasServerRenderedLines()) loadPharmacyRequestByNo();
});
