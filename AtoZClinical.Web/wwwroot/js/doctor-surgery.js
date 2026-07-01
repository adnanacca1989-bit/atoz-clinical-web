document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#surgeryPatientNameInput',
        fieldMap: {
            '[name="Input.PatientBarcode"]': p => p.patientNo || '',
            '[name="Input.Age"]': p => p.age != null ? p.age : '',
            '[name="Input.City"]': p => p.city || '',
            '[name="Input.NationalId"]': p => p.nationalId || '',
            '[name="Input.Phone"]': p => p.phone || '',
            '[name="Input.MotherName"]': p => p.motherName || '',
            '[name="Input.DoctorName"]': p => p.doctorName || '',
            '[name="Input.Specialty"]': p => p.specialty || '',
            '[name="Input.PatientRecordId"]': p => p.id || ''
        }
    });

    initDoctorPicker({
        doctorNameSelector: '#surgeryDoctorNameInput',
        fieldMap: {
            '[name="Input.DoctorRecordId"]': d => d.id || '',
            '[name="Input.Specialty"]': d => d.specialty || ''
        }
    });

    initPatientBarcodeScanner({
        barcodeSelector: '#surgeryPatientBarcodeInput',
        patientNameSelector: '#surgeryPatientNameInput',
        fieldMap: {
            '[name="Input.PatientBarcode"]': p => p.patientNo || '',
            '[name="Input.Age"]': p => p.age != null ? p.age : '',
            '[name="Input.City"]': p => p.city || '',
            '[name="Input.NationalId"]': p => p.nationalId || '',
            '[name="Input.Phone"]': p => p.phone || '',
            '[name="Input.MotherName"]': p => p.motherName || '',
            '[name="Input.DoctorName"]': p => p.doctorName || '',
            '[name="Input.Specialty"]': p => p.specialty || '',
            '[name="Input.PatientRecordId"]': p => p.id || ''
        }
    });
});
