document.addEventListener('DOMContentLoaded', () => {
    initPatientPicker({
        patientNameSelector: '#cashReceiptPatientNameInput',
        fieldMap: {
            ...standardPatientFieldMap(true),
            '[name="Input.AppointmentDate"]': p => p.appointmentDate || '',
            '[name="Input.AppointmentTime"]': p => p.appointmentTime || ''
        }
    });
});
