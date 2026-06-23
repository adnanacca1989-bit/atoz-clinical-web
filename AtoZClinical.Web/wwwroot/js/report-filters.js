/** Patient/doctor pickers and barcode scan for clinical report filter forms. */
function initReportPickers(options = {}) {
    const patientSelector = options.patientSelector || '#reportPatientNameInput';
    const doctorSelector = options.doctorSelector || '#reportDoctorNameInput';
    const barcodeSelector = options.barcodeSelector || '#reportPatientBarcodeInput';
    const runSubmitSelector = options.runSubmitSelector || '#reportRunSubmit';
    const autoRunOnPatient = options.autoRunOnPatient !== false;

    const patientInput = document.querySelector(patientSelector);
    const doctorInput = document.querySelector(doctorSelector);
    const barcodeInput = document.querySelector(barcodeSelector);

    const patientFieldMap = options.patientFieldMap || {};
    if (barcodeInput && !patientFieldMap[barcodeSelector]) {
        patientFieldMap[barcodeSelector] = p => p.patientNo || '';
    }

    if (patientInput) {
        initPatientPicker({
            patientNameSelector: patientSelector,
            fieldMap: patientFieldMap,
            onApply: autoRunOnPatient
                ? () => document.querySelector(runSubmitSelector)?.click()
                : undefined
        });
    }

    if (barcodeInput) {
        initPatientBarcodeScanner({
            barcodeSelector,
            patientNameSelector: patientSelector,
            fieldMap: patientFieldMap,
            onApply: autoRunOnPatient
                ? () => document.querySelector(runSubmitSelector)?.click()
                : undefined
        });
    }

    if (doctorInput) {
        initDoctorPicker({
            doctorNameSelector: doctorSelector
        });
    }

    document.getElementById('reportOpenPatientBtn')?.addEventListener('click', (e) => {
        e.preventDefault();
        patientInput?.click();
    });
}
