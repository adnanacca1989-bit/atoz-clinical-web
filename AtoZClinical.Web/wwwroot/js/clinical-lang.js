(function () {
    const lang = document.documentElement.getAttribute('lang') || 'en';
    if (lang !== 'ar') return;

    const map = {
        'A to Z Clinical': 'أ تو زد كلينيكال',
        'Logout': 'تسجيل الخروج',
        'Search': 'بحث',
        'Reminders': 'تذكيرات',
        'Notifications': 'إشعارات',
        'Clear': 'مسح',
        'Company Profile': 'ملف الشركة',
        'Settings': 'الإعدادات',
        'System Settings': 'إعدادات النظام'
    };

    document.querySelectorAll('.atz-brand, .btn, .dropdown-header, .dropdown-item, .navbar-user-name').forEach(el => {
        const text = (el.textContent || '').trim();
        if (map[text]) el.textContent = map[text];
    });
})();
