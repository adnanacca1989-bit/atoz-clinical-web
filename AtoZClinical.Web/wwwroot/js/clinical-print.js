/** Print clinical forms and reports as A4 pages. */
function clinicalPrintForm() {
    document.body.classList.add('clinical-form-page-printing');
    window.print();
    window.addEventListener('afterprint', () => document.body.classList.remove('clinical-form-page-printing'), { once: true });
}

function clinicalPrintReport() {
    document.body.classList.add('clinical-report-page-printing');
    window.print();
    window.addEventListener('afterprint', () => document.body.classList.remove('clinical-report-page-printing'), { once: true });
}

function openPatientPrintBundle(patientName, patientId, doctorName) {
    if (!patientName?.trim() && !patientId?.trim()) {
        alert('Please select a patient before printing all reports.');
        return;
    }
    const params = new URLSearchParams();
    if (patientName?.trim()) params.set('patientName', patientName.trim());
    if (patientId?.trim()) params.set('patientId', patientId.trim());
    if (doctorName?.trim()) params.set('doctorName', doctorName.trim());
    window.open(`/Reports/PatientPrintBundle?${params}`, '_blank');
}
