(function () {
    const AR = {
        'A to Z Clinical': 'أ تو زد كلينيكال',
        'Logout': 'تسجيل الخروج',
        'Search': 'بحث',
        'Reminders': 'تذكيرات',
        'Notifications': 'إشعارات',
        'Clear': 'مسح',
        'Close': 'إغلاق',
        'Company Profile': 'ملف الشركة',
        'Settings': 'الإعدادات',
        'System Settings': 'إعدادات النظام',
        'Dashboard': 'لوحة التحكم',
        'Workflow': 'سير العمل',
        'Patient Registration': 'تسجيل المريض',
        'Doctor Registration': 'تسجيل الطبيب',
        "Doctor's Prescription": 'وصفة الطبيب',
        'LABORATORY': 'المختبر',
        'Lab Registration': 'تسجيل المختبر',
        'Lab Request': 'طلب مختبر',
        'Lab Result': 'نتيجة المختبر',
        'RADIOLOGY': 'الأشعة',
        'Radiology Registration': 'تسجيل الأشعة',
        'Radiology Request': 'طلب أشعة',
        'Radiology Result': 'نتيجة الأشعة',
        'PHARMACY': 'الصيدلية',
        'Pharmacy Item Registration': 'تسجيل أصناف الصيدلية',
        'Pharmacy Request': 'طلب صيدلية',
        'Pharmacy Bill': 'فاتورة الصيدلية',
        'Pharmacy Purchase': 'شراء الصيدلية',
        'Pharmacy Opening Balance': 'رصيد افتتاحي للصيدلية',
        'BILLING': 'الفوترة',
        'Invoice / Billing': 'فاتورة / الفوترة',
        'Cash Receipt': 'سند قبض',
        'Cash Payment': 'سند دفع',
        'Chart of Accounts': 'دليل الحسابات',
        'Service Income': 'إيرادات الخدمات',
        'REPORTS': 'التقارير',
        'Accounts Receivable': 'الذمم المدينة',
        'Accounts Payable': 'الذمم الدائنة',
        'PL Statement': 'قائمة الأرباح والخسائر',
        'Balance Sheet': 'الميزانية العمومية',
        'Cash Report': 'تقرير النقدية',
        'Operating Report': 'تقرير التشغيل',
        'Cost of Goods Sold': 'تكلفة البضاعة المباعة',
        'Pharmacy Inventory': 'مخزون الصيدلية',
        'Doctor Report': 'تقرير الأطباء',
        'Patient History': 'سجل المريض',
        'Patient Status Report': 'تقرير حالة المريض',
        'Appointment Reminders': 'تذكيرات المواعيد',
        'Select Patient': 'اختيار المريض',
        'Patient Search': 'بحث المريض',
        'From Date': 'من تاريخ',
        'To Date': 'إلى تاريخ',
        'Status': 'الحالة',
        'Sort By': 'ترتيب حسب',
        'All': 'الكل',
        'Pending': 'قيد الانتظار',
        'Confirmed': 'مؤكد',
        'Under Process': 'قيد المعالجة',
        'Completed': 'مكتمل',
        'Cancelled': 'ملغي',
        'Most Recent': 'الأحدث',
        'Name A to Z': 'الاسم أ إلى ي',
        'City A to Z': 'المدينة أ إلى ي',
        'Patient ID': 'رقم المريض',
        'Patient Name': 'اسم المريض',
        'Mother Name': 'اسم الأم',
        'Gender': 'الجنس',
        'Age': 'العمر',
        'Phone': 'الهاتف',
        'City': 'المدينة',
        'Doctor Name': 'اسم الطبيب',
        'Specialty': 'التخصص',
        'Appt. Date': 'تاريخ الموعد',
        'Appt. Time': 'وقت الموعد',
        'Add': 'إضافة',
        'Refresh': 'تحديث',
        'Run': 'تشغيل',
        'Filter': 'تصفية',
        'Export to Excel': 'تصدير إلى إكسل',
        'Print': 'طباعة',
        'New': 'جديد',
        'Edit': 'تعديل',
        'Delete': 'حذف',
        'Back': 'رجوع',
        'Next': 'التالي',
        '+ New': '+ جديد',
        'Record Transaction': 'سجل المعاملات',
        'Payment Method': 'طريقة الدفع',
        'Transaction Type': 'نوع المعاملة',
        'Patient Barcode': 'باركود المريض',
        'Cash Receipt': 'سند قبض',
        'Cash Payment': 'سند دفع',
        'Total Invoice': 'إجمالي الفاتورة',
        'Discount': 'الخصم',
        'Ending Balance': 'الرصيد النهائي',
        'Invoice ID': 'رقم الفاتورة',
        'Invoice Date': 'تاريخ الفاتورة',
        'Aging Days': 'أيام التقادم',
        'Totals': 'الإجماليات',
        'Receipt': 'قبض',
        'Payment': 'دفع',
        'Cash': 'نقدي',
        'Card': 'بطاقة',
        'Bank Transfer': 'تحويل بنكي',
        'Cheque': 'شيك',
        'Health Insurance Co': 'شركة التأمين الصحي',
        'Paid': 'مدفوع',
        'Partial': 'جزئي',
        'Unpaid': 'غير مدفوع',
        'Male': 'ذكر',
        'Female': 'أنثى',
        'Years': 'سنة',
        'Years': 'سنوات',
        'Profit and Loss': 'الأرباح والخسائر',
        'Income': 'الإيرادات',
        'Expenses': 'المصروفات',
        'Gross Profit': 'إجمالي الربح',
        'Net Income': 'صافي الدخل',
        'Total Income': 'إجمالي الإيرادات',
        'Total Expenses': 'إجمالي المصروفات',
        'Account / Detail': 'الحساب / التفاصيل',
        'TOTAL': 'الإجمالي',
        'Consultation Revenue': 'إيرادات الاستشارة',
        'Lab Revenue': 'إيرادات المختبر',
        'Radiology Revenue': 'إيرادات الأشعة',
        'Pharmacy Bill Revenue': 'إيرادات فواتير الصيدلية',
        'Search patient by ID, name, phone, city, doctor, specialty...': 'ابحث عن المريض بالرقم أو الاسم أو الهاتف أو المدينة أو الطبيب أو التخصص...'
    };

    function translateText(text) {
        const trimmed = (text || '').trim();
        if (!trimmed) return text;
        if (AR[trimmed]) return AR[trimmed];
        if (trimmed.endsWith(' Years') && AR[trimmed.replace(' Years', '')]) {
            return `${AR[trimmed.replace(' Years', '')]} سنوات`;
        }
        return AR[trimmed] || text;
    }

    function isClinicalArabic() {
        return (document.documentElement.getAttribute('lang') || 'en').toLowerCase() === 'ar';
    }

    function applyClinicalArabic(root) {
        const scope = root || document;
        scope.querySelectorAll('[data-i18n]').forEach(el => {
            const key = el.getAttribute('data-i18n');
            if (key && AR[key]) el.textContent = AR[key];
        });
        scope.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
            const key = el.getAttribute('data-i18n-placeholder');
            if (key && AR[key]) el.setAttribute('placeholder', AR[key]);
        });
        scope.querySelectorAll('.sidebar-link, .btn-form, .page-title, .clinical-field-label, .record-transaction-header, .atz-section-title, .report-hint strong').forEach(el => {
            const text = (el.textContent || '').trim();
            if (AR[text]) el.textContent = AR[text];
        });
        scope.querySelectorAll('table thead th').forEach(th => {
            const text = (th.textContent || '').trim();
            if (AR[text]) th.textContent = AR[text];
        });
        scope.querySelectorAll('.clinical-toolbar button, .clinical-toolbar a.btn-form').forEach(btn => {
            const text = (btn.textContent || '').replace(/[💾✏️🗑️🔍🔄🧹🖨️✕←→+]/g, '').trim();
            if (AR[text]) {
                const prefix = (btn.textContent || '').match(/^[^\w]+/)?.[0] || '';
                btn.textContent = prefix + AR[text];
            }
        });
        scope.querySelectorAll('.dropdown-item, .dropdown-header, .navbar .btn').forEach(el => {
            const text = (el.textContent || '').trim();
            if (AR[text]) el.textContent = AR[text];
        });
        scope.querySelectorAll('option').forEach(opt => {
            const text = (opt.textContent || '').trim();
            if (AR[text]) opt.textContent = AR[text];
        });
        scope.querySelectorAll('table tbody td').forEach(td => {
            const text = (td.textContent || '').trim();
            if (AR[text]) td.textContent = AR[text];
        });
    }

    window.applyClinicalArabic = applyClinicalArabic;
    window.isClinicalArabic = isClinicalArabic;

    document.addEventListener('DOMContentLoaded', () => {
        if (!isClinicalArabic()) return;
        applyClinicalArabic(document);
    });
})();
