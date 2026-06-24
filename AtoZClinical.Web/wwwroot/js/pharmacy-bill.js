document.addEventListener('DOMContentLoaded', () => {

    const setLineField = (index, field, value) => {

        const el = document.querySelector(`[name="Lines[${index}].${field}"]`);

        if (el) el.value = value ?? '';

    };



    const syncItemSelect = (row, barcode) => {

        if (!row || !barcode) return;

        const select = row.querySelector('.pharmacy-item-select');

        if (!select) return;

        for (const opt of select.options) {

            if (opt.dataset.barcode === barcode || opt.value === barcode) {

                select.value = opt.value;

                break;

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

            setLineField(i, 'LineNo', line.lineNo ?? i + 1);

            setLineField(i, 'Barcode', line.barcode);

            setLineField(i, 'MedicineCode', line.medicineCode);

            setLineField(i, 'MedicineName', line.medicineName);

            setLineField(i, 'Dosage', line.dosage);

            setLineField(i, 'Uom', line.uom);

            setLineField(i, 'Qty', line.qty);

            setLineField(i, 'UnitPrice', line.unitPrice);

            const row = document.querySelector(`.pharmacy-line[data-line="${i}"]`);

            syncItemSelect(row, line.barcode);

            const totalEl = row?.querySelector('.pharmacy-line-total');

            if (totalEl) {

                const total = line.total ?? (Number(line.qty || 0) * Number(line.unitPrice || 0));

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



    const loadPharmacyRequest = async (patient) => {

        const params = new URLSearchParams();

        const requestNoOnForm = document.querySelector('[name="Input.RequestNo"]')?.value?.trim();

        if (requestNoOnForm) params.set('requestNo', requestNoOnForm);

        if (patient?.patientNo) params.set('patientId', patient.patientNo);

        if (patient?.name) params.set('patientName', patient.name);

        if (!params.has('requestNo') && !params.has('patientId') && !params.has('patientName')) return;

        try {

            const res = await fetch(`/Pharmacy/RequestByPatient?${params}`);

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

            const res = await fetch(`/Pharmacy/RequestByPatient?${params}`);

            if (!res.ok) return;

            const data = await res.json();

            applyRequestData(data);

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

    const patientName = document.querySelector('#pharmacyBillPatientNameInput')?.value?.trim();
    const patientId = document.querySelector('[name="Input.PatientId"]')?.value?.trim();
    const requestNo = document.querySelector('[name="Input.RequestNo"]')?.value?.trim();
    if (requestNo) loadPharmacyRequestByNo();
    else if (patientName || patientId) loadPharmacyRequest({ patientNo: patientId, name: patientName });
});

