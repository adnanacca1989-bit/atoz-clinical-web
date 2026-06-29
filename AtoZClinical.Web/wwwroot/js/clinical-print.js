/** Print clinical forms and reports as A4 pages. */
function clinicalPrintForm() {
    document.body.classList.add('clinical-form-page-printing');
    window.print();
    window.addEventListener('afterprint', () => document.body.classList.remove('clinical-form-page-printing'), { once: true });
}

function clinicalPrintReport() {
    document.body.classList.add('clinical-report-page-printing');
    if (document.getElementById('print-area'))
        document.body.classList.add('clinical-print-area-active');
    window.print();
    window.addEventListener('afterprint', () => {
        document.body.classList.remove('clinical-report-page-printing', 'clinical-print-area-active');
    }, { once: true });
}

/** Print only the element with id print-area (or a custom selector). */
function clinicalPrintArea(selector) {
    const target = selector ? document.querySelector(selector) : document.getElementById('print-area');
    if (!target) {
        clinicalPrintReport();
        return;
    }
    if (!target.id)
        target.id = 'print-area';
    document.body.classList.add('clinical-print-area-active');
    window.print();
    window.addEventListener('afterprint', () => {
        document.body.classList.remove('clinical-print-area-active');
    }, { once: true });
}

function openPatientPrintBundle(patientName, patientId, doctorName, section) {
    if (!patientName?.trim() && !patientId?.trim()) {
        alert('Please select a patient before printing all reports.');
        return;
    }
    const params = new URLSearchParams();
    if (patientName?.trim()) params.set('patientName', patientName.trim());
    if (patientId?.trim()) params.set('patientId', patientId.trim());
    if (doctorName?.trim()) params.set('doctorName', doctorName.trim());
    if (section?.trim()) params.set('section', section.trim());
    window.open(`/Reports/PatientPrintBundle?${params}`, '_blank');
}
