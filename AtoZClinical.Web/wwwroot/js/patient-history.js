document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#patientHistoryPatientInput',
        fieldMap: {
            '#patientHistoryPatientIdInput': p => p.patientNo || '',
            '#patientHistoryAgeInput': p => p.age != null ? p.age : '',
            '#patientHistoryPhoneInput': p => p.phone || '',
            '#patientHistoryCityInput': p => p.city || '',
            '#patientHistoryDoctorInput': p => p.doctorName || ''
        },
        onApply: () => document.getElementById('patientHistoryRunSubmit')?.click()
    });

    initDoctorPicker({
        doctorNameSelector: '#patientHistoryDoctorInput'
    });

    document.getElementById('patientHistorySearchPatientBtn')?.addEventListener('click', () => {
        document.getElementById('patientHistoryPatientInput')?.click();
    });

    document.getElementById('patientHistoryPrintAllBtn')?.addEventListener('click', () => {
        const patientName = document.querySelector('#patientHistoryPatientInput')?.value || '';
        const patientId = document.querySelector('#patientHistoryPatientIdInput')?.value || '';
        const doctorName = document.querySelector('#patientHistoryDoctorInput')?.value || '';
        openPatientPrintBundle(patientName, patientId, doctorName);
    });
});
