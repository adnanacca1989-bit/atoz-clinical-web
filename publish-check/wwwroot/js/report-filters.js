/** Patient/doctor pickers for clinical report filter forms. */
function initReportPickers(options = {}) {
    const patientSelector = options.patientSelector || '#reportPatientNameInput';
    const doctorSelector = options.doctorSelector || '#reportDoctorNameInput';
    const runSubmitSelector = options.runSubmitSelector || '#reportRunSubmit';
    const autoRunOnPatient = options.autoRunOnPatient !== false;

    const patientInput = document.querySelector(patientSelector);
    const doctorInput = document.querySelector(doctorSelector);

    if (patientInput) {
        initPatientPicker({
            patientNameSelector: patientSelector,
            fieldMap: options.patientFieldMap || {},
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
