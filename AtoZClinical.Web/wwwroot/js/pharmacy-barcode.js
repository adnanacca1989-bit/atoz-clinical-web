document.addEventListener('DOMContentLoaded', () => {

    const fillUomSelect = (selectEl, baseUom, alternateUom, selected) => {

        if (!selectEl) return;

        selectEl.innerHTML = '';

        const add = (u) => {

            const o = document.createElement('option');

            o.value = u;

            o.textContent = u;

            if (u === selected) o.selected = true;

            selectEl.appendChild(o);

        };

        add(baseUom || 'Pcs');

        if (alternateUom && alternateUom !== baseUom) add(alternateUom);

    };



    const applyItemToRow = (row, data) => {

        const set = (cls, val) => {

            const el = row.querySelector(cls);

            if (el) el.value = val ?? '';

        };

        set('.pharmacy-barcode', data.barcode);

        set('.pharmacy-code', data.medicineCode);

        set('.pharmacy-name', data.medicineName);

        set('.pharmacy-dosage', data.dosage);

        fillUomSelect(row.querySelector('.pharmacy-uom'), data.baseUom, data.alternateUom, data.baseUom || 'Pcs');



        const costEl = row.querySelector('.pharmacy-cost');

        const priceEl = row.querySelector('.pharmacy-price');

        if (costEl) {

            if (data.movingAverageCost > 0) costEl.value = Number(data.movingAverageCost).toFixed(2);

            else if (data.defaultUnitPrice > 0) costEl.value = Number(data.defaultUnitPrice).toFixed(2);

        }

        if (priceEl) {

            if (data.defaultUnitPrice > 0) priceEl.value = Number(data.defaultUnitPrice).toFixed(2);

            else if (data.movingAverageCost > 0) priceEl.value = Number(data.movingAverageCost).toFixed(2);

        }



        const stockEl = row.querySelector('.pharmacy-stock');

        if (stockEl && data.qtyOnHand !== undefined)

            stockEl.textContent = `Stock: ${data.qtyOnHand}`;

    };



    document.querySelectorAll('.pharmacy-line').forEach(row => {

        const itemSelect = row.querySelector('.pharmacy-item-select');

        const applySelectedItem = () => {
            if (!itemSelect) return;
            const opt = itemSelect.selectedOptions[0];
            if (!opt || !opt.dataset.name) return;
            applyItemToRow(row, {
                barcode: opt.dataset.barcode,
                medicineCode: opt.dataset.code,
                medicineName: opt.dataset.name,
                dosage: opt.dataset.dosage,
                baseUom: opt.dataset.baseUom,
                alternateUom: opt.dataset.altUom,
                defaultUnitPrice: parseFloat(opt.dataset.price || '0'),
                movingAverageCost: parseFloat(opt.dataset.avgCost || '0'),
                qtyOnHand: parseInt(opt.dataset.qty || '0', 10)
            });
        };

        if (itemSelect) {
            itemSelect.addEventListener('change', applySelectedItem);
        }



        const barcodeInput = row.querySelector('.pharmacy-barcode');

        if (!barcodeInput) return;



        barcodeInput.addEventListener('blur', async () => {

            const barcode = barcodeInput.value.trim();

            if (!barcode) return;



            try {

                const res = await fetch(`/Pharmacy/ItemLookup?barcode=${encodeURIComponent(barcode)}`);

                if (!res.ok) return;

                const data = await res.json();

                if (!data.found) return;

                applyItemToRow(row, data);



                if (itemSelect) {

                    for (const opt of itemSelect.options) {

                        if (opt.dataset.barcode === data.barcode) {

                            itemSelect.value = opt.value;

                            break;

                        }

                    }

                }

            } catch {

                /* ignore lookup errors */

            }

        });

    });

    document.getElementById('purchaseBillForm')?.addEventListener('submit', () => {
        document.querySelectorAll('.pharmacy-item-select').forEach(select => {
            if (select.value) select.dispatchEvent(new Event('change'));
        });
    });

});

