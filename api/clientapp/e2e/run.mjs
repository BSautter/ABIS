// End-to-end check: drive the *generated* client against a live API instance.
// Run by CI after the client is regenerated + compiled, so a contract change that
// breaks the real request/response path (not just compilation) fails the build.
//
// Usage: start the API (seeded SQLite), then:
//   ABIS_BASE=http://127.0.0.1:5225 ABIS_KEY=<key> node clientapp/e2e/run.mjs
import { test } from 'node:test';
import assert from 'node:assert/strict';
import {
  AbisClient,
  ApiException,
  OrderCreateWithItems,
  CustomerOrderWrite,
  OrderItemWrite,
  CoilPatch,
  ShipmentStatusPatch,
  MaintLogWrite,
  DieWrite,
  DowntimeInstanceWrite,
  ReceivingBolWrite,
  ReceivingBolCoilWrite,
  CustomerWrite,
  CustomerContactWrite,
  PartWrite,
  CarrierWrite,
  ScanLogWrite,
  SketchWrite,
  ShiftWrite,
  JobPatch,
  SheetSkidWrite,
  ScrapSkidWrite,
  SheetSkidWarehousePatch,
  SalesReminderWrite,
  SalesProbabilityWrite,
  CoilOwnershipTransferWrite,
  SecurityUserWrite,
  SecurityGroupWrite,
  SecurityApplicationWrite,
  GrantWrite,
  DimensionCheckWrite,
  EvalScrapWrite,
  JobFolderNoteWrite,
  LineErrorWrite,
  PmWrite,
  PmActionWrite,
  PmCompleteWrite,
} from '../../src/ABIS.Api/wwwroot/ui/app/generated/abis-client.js';

const base = process.env.ABIS_BASE ?? 'http://127.0.0.1:5225';
const key = process.env.ABIS_KEY ?? 'dev-local-key';

// A client that injects the API key on every request (baseUrl points at the API).
const client = new AbisClient(base, {
  fetch: (url, init = {}) => {
    const headers = new Headers(init.headers);
    headers.set('X-Api-Key', key);
    return fetch(url, { ...init, headers });
  },
});

test('listCoils returns typed, server-sorted results', async () => {
  const page = await client.listCoils(1, 50, undefined, undefined, undefined, undefined, undefined, undefined, 'netWt', 'asc');
  assert.equal(page.totalCount, 7); // 5001–5004 + Cliffs 846 coil 4962 + order-coil coils 4801/4802 (4803 status 13 excluded)
  assert.equal(page.items[0].coilAbcNum, 5003); // lowest net_wt first
});

test('getCoil returns a typed entity', async () => {
  const c = await client.getCoil(5004);
  assert.equal(c.coilAlloy2, '5052');
  assert.equal(c.coilStatus, 3);
});

test('getCoil(missing) throws a typed ApiException(404)', async () => {
  await assert.rejects(
    () => client.getCoil(999999),
    (e) => e instanceof ApiException && e.status === 404
  );
});

test('listJobs filters + sorts via the typed client', async () => {
  const page = await client.listJobs(1, 25, undefined, undefined, undefined, 'jobStatus', 'desc');
  const statuses = page.items.map((j) => j.jobStatus);
  assert.deepEqual(statuses, [...statuses].sort((a, b) => b - a));
});

test('createOrderWithItems writes via typed DTOs and returns a typed OrderDetail', async () => {
  // Back-check widening: the full customer_order header + order_item line round-trip
  // (quantity/tolerance, item_status, notes, packaging, part linkage) — columns SQLite
  // CI previously never exercised.
  const body = new OrderCreateWithItems({
    order: new CustomerOrderWrite({
      origCustomerId: 4001, enduserId: 4002, origCustomerPo: 'PO-E2E', enduserPo: 'EPO-E2E',
      orderType: 1, term: 'NET30', salesOrder: 'SO-E2E', custOrderNote: 'rush',
    }),
    items: [new OrderItemWrite({
      enduserPartNum: 'PN-E2E', alloy2: '3003', sheetType: 'FLAT', gauge: 0.05,
      quantity: 1000, quantityPlus: 50, quantityMinus: 25, itemStatus: 1, maxSkidWt: 4000,
      unitPrice: 1.2345, itemNote: 'line note', packagingSpec1: 'wrap', partNumId: 6001,
      // trimming_required='Y' now enforces the edge-trim tolerance, so supply valid trim data
      // (incoming - trimmed = 2.0", within the 1.5"-12" trimmer window).
      trimmingRequired: 'Y', incomingCoilWidth: 50, trimmedCoilWidth: 48, trimTypeCode: 1,
    })],
  });
  const detail = await client.createOrderWithItems(body);
  assert.ok(detail.order.orderAbcNum > 0);
  assert.equal(detail.order.enduserId, 4002);
  assert.equal(detail.order.term, 'NET30');
  assert.equal(detail.order.salesOrder, 'SO-E2E');
  assert.equal(detail.items.length, 1);
  assert.equal(detail.items[0].enduserPartNum, 'PN-E2E');
  assert.equal(detail.items[0].quantity, 1000);
  assert.equal(detail.items[0].quantityPlus, 50);
  assert.equal(detail.items[0].itemStatus, 1);
  assert.equal(detail.items[0].unitPrice, 1.2345);
  assert.equal(detail.items[0].itemNote, 'line note');
  assert.equal(detail.items[0].partNumId, 6001);
  assert.equal(detail.items[0].trimmingRequired, 'Y');
});

// The downtime SPA's flow: create an instance, then load + replace it (typed).
test('downtime flow: create, get, update instance (typed)', async () => {
  const created = await client.createDowntimeInstance(new DowntimeInstanceWrite({
    abJobNum: 25000, lineNum: 1, startingTime: new Date(), note: 'E2E downtime',
  }));
  assert.ok(created.instanceNum > 0);
  const got = await client.getDowntimeInstance(created.instanceNum);
  assert.equal(got.instanceNum, created.instanceNum);
  const updated = await client.updateDowntimeInstance(created.instanceNum, new DowntimeInstanceWrite({
    abJobNum: 25000, lineNum: 1, startingTime: new Date(), note: 'E2E downtime edited',
  }));
  assert.equal(updated.note, 'E2E downtime edited');
});

// The dies SPA's flow: create a die, then load + replace it (typed).
test('dies flow: create, get, update die (typed)', async () => {
  // Back-check widening: owner + the change-time / engineered-scrap columns round-trip.
  const created = await client.createDie(new DieWrite({
    dieName: 'E2E-DIE', owner: 'E2E-OWN', status: 0, toolNum: 'T-E2E',
    engineeredScrapYN: 'Y', numOfPartsPerHit: 3, angleChangeMinutes: 12, averageDieChangeMinutes: 40,
  }));
  assert.ok(created.dieId > 0);
  assert.equal(created.owner, 'E2E-OWN');
  assert.equal(created.engineeredScrapYN, 'Y');
  assert.equal(created.numOfPartsPerHit, 3);
  const got = await client.getDie(created.dieId);
  assert.equal(got.dieId, created.dieId);
  assert.equal(got.averageDieChangeMinutes, 40);
  const updated = await client.updateDie(created.dieId, new DieWrite({
    dieName: 'E2E-DIE', owner: 'E2E-OWN-2', status: 1, location: 'BAY-9', numOfPartsPerHit: 4,
  }));
  assert.equal(updated.location, 'BAY-9');
  assert.equal(updated.owner, 'E2E-OWN-2');
  assert.equal(updated.numOfPartsPerHit, 4);
});

// The receiving SPA's flow: create a BOL, then load + replace it (typed).
test('receiving flow: create, get, update receiving BOL (typed)', async () => {
  const created = await client.createReceivingBol(new ReceivingBolWrite({
    bol: 'E2E-BOL', customerId: 4001, createdBy: 'e2e', status: 0,
  }));
  assert.ok(created.receivingBolId > 0);
  const got = await client.getReceivingBol(created.receivingBolId);
  assert.equal(got.bol, 'E2E-BOL');
  const updated = await client.updateReceivingBol(created.receivingBolId, new ReceivingBolWrite({
    bol: 'E2E-BOL', customerId: 4001, createdBy: 'e2e', status: 1,
  }));
  assert.equal(updated.status, 1);
});

// The OPC log flow: sessions, their captured readings (host/device/item), and the item catalog (typed).
test('opc-log flow: sessions, readings, item catalog (typed)', async () => {
  const logs = await client.getOpcLogs();
  assert.ok(Array.isArray(logs) && logs.length > 0);
  const details = await client.getOpcLogDetails(1);
  assert.ok(details.length > 0);
  assert.ok(details.every((d) => typeof d.itemName === 'string' && typeof d.remoteHost === 'string'));
  const items = await client.getOpcItems();
  assert.ok(items.includes('Line110.Status'));
});

// The quality/recovery flow: defect catalog, product types, recovery customers, per-customer defects (typed).
test('quality flow: recovery catalog + customer defects (typed)', async () => {
  const scrap = await client.getScrapTypes();
  assert.ok(Array.isArray(scrap) && scrap.length > 0);
  const prods = await client.getProductTypes();
  assert.ok(Array.isArray(prods) && prods.length > 0);
  const custs = await client.getRecoveryCustomers();
  assert.ok(custs.some((c) => c.customerId === 4001));
  const defects = await client.getCustomerDefects(4001);
  assert.equal(defects.length, 2);
  assert.ok(defects.every((d) => typeof d.scrapCode === 'string'));
});

// The reporting flow: per-line production summary aggregation (typed, read-only).
test('reporting flow: production summary by line (typed)', async () => {
  const rows = await client.getProductionSummary(undefined, undefined);
  assert.ok(Array.isArray(rows) && rows.length > 0);
  // Every line row carries a numeric job count; at least one line has run jobs.
  assert.ok(rows.every((r) => typeof r.jobCount === 'number'));
  assert.ok(rows.some((r) => r.jobCount > 0));
});

// The accounting/invoicing flow: a job's rejected/rebanded coils that drive its invoice (typed).
test('accounting flow: invoice rej/reband coils for a job (typed)', async () => {
  const coils = await client.getInvoiceCoils(1002);
  assert.ok(Array.isArray(coils) && coils.length > 0);
  assert.ok(coils.every((c) => c.processCoilStatus === 3 || c.processCoilStatus === 7));
  // Select by status, not by position: the list is ordered coil_abc_num DESC, so an index quietly
  // points at a different coil as soon as the job has both a rejected and a rebanded coil.
  assert.equal(coils.find((c) => c.processCoilStatus === 3).coilAbcNum, 5003);
  assert.equal(coils.find((c) => c.processCoilStatus === 7).coilAbcNum, 5002);
});

// The warehouse SPA's flow: list sheet skids + a warehouse update (location/ticket/status) (typed).
test('warehouse flow: list sheet skids + warehouse update (typed)', async () => {
  const list = await client.listSheetSkids(1, 5, undefined, undefined);
  assert.ok(Array.isArray(list.items));
  const updated = await client.updateSheetSkidWarehouse(3001, new SheetSkidWarehousePatch({
    skidLocation: 'WH-E2E', skidTicketIfWhed: 'T-E2E', skidSheetStatus: 2,
  }));
  assert.equal(updated.skidLocation, 'WH-E2E');
  assert.equal(updated.skidSheetStatus, 2);
});

// The EDI monitor's flow: list transactions, log, customer-edi setup, and types (typed, read-only).
test('edi flow: list transactions, log, customer-edi, types (typed)', async () => {
  const tx = await client.listEdiTransactions(1, 5, undefined, undefined, undefined, undefined);
  assert.ok(Array.isArray(tx.items));
  const log = await client.listEdiLog(1, 5, undefined, undefined, undefined);
  assert.ok(Array.isArray(log.items));
  const types = await client.listEdiTypes();
  assert.ok(Array.isArray(types));
  const custEdi = await client.listCustomerEdi();
  assert.ok(Array.isArray(custEdi));
});

// The skids SPA's flow: list each skid type + create a sheet and a scrap skid (typed).
test('skids flow: list + create sheet & scrap skids (typed)', async () => {
  const sheet = await client.listSheetSkids(1, 5, undefined, undefined);
  assert.ok(Array.isArray(sheet.items));
  const createdSheet = await client.createSheetSkid(new SheetSkidWrite({
    abJobNum: 1001, sheetSkidDisplayNum: 'E2E-1', sheetNetWt: 100, skidPieces: 50,
  }));
  assert.ok(createdSheet.sheetSkidNum > 0);
  const createdScrap = await client.createScrapSkid(new ScrapSkidWrite({
    scrapAbJobNum: '1001', scrapAlloy2: '3003', scrapNetWt: 10, scrapType: 1,
  }));
  assert.ok(createdScrap.scrapSkidNum > 0);
  const partials = await client.listPartialSkids(1, 5, undefined, undefined);
  assert.ok(Array.isArray(partials.items));
});

// The production-jobs SPA's flow: list, open a job + its contents, patch it (typed).
test('jobs flow: list, get, children, patch (typed)', async () => {
  const page = await client.listJobs(1, 5, undefined, undefined, undefined, undefined, undefined);
  assert.ok(page.items.length > 0);
  const job = await client.getJob(1001);
  assert.equal(job.abJobNum, 1001);
  const coils = await client.getJobCoils(1001);
  assert.ok(Array.isArray(coils) && coils.length > 0);
  const skids = await client.getJobSkids(1001);
  assert.ok(Array.isArray(skids));
  const patched = await client.patchJob(1001, new JobPatch({ jobStatus: 2, numberOfMenUsed: 4 }));
  assert.equal(patched.jobStatus, 2);
});

// The shifts SPA's flow: create a shift, then load + replace it (typed).
test('shifts flow: create, get, update shift (typed)', async () => {
  const created = await client.createShift(new ShiftWrite({
    startTime: new Date(), lineNum: 110, operatorInitial: 'E2E', dtTotal: 0.5, shiftDataStatus: 1,
  }));
  assert.ok(created.shiftNum > 0);
  const got = await client.getShift(created.shiftNum);
  assert.equal(got.lineNum, 110);
  const updated = await client.updateShift(created.shiftNum, new ShiftWrite({
    startTime: new Date(), lineNum: 110, operatorInitial: 'E2E', dtTotal: 1.25, shiftDataStatus: 2,
  }));
  assert.equal(updated.shiftDataStatus, 2);
});

// The sketches SPA's flow: create a sketch, then load + replace it (typed).
test('sketches flow: create, get, update sketch (typed)', async () => {
  const created = await client.createSketch(new SketchWrite({
    sketchName: 'E2E-SKETCH', sketchStatus: 1, sketchNotes: 'e2e notes',
  }));
  assert.ok(created.sketchId > 0);
  const got = await client.getSketch(created.sketchId);
  assert.equal(got.sketchName, 'E2E-SKETCH');
  const updated = await client.updateSketch(created.sketchId, new SketchWrite({
    sketchName: 'E2E-SKETCH', sketchStatus: 0, sketchNotes: 'updated',
  }));
  assert.equal(updated.sketchStatus, 0);
});

// The scan SPA's flow: record a scan (append-only), then read it + list by job (typed).
test('scan flow: record a scan, get, list by job (typed)', async () => {
  const created = await client.createScanLog(new ScanLogWrite({
    abJobNum: 1001, scanStation: 'E2E-ST', note: 'e2e scan',
  }));
  assert.ok(created.scanId > 0);
  const got = await client.getScanLog(created.scanId);
  assert.equal(got.note, 'e2e scan');
  const page = await client.listScanLogs(1, 50, 1001, 'scanDatetime', 'desc');
  assert.ok(page.items.some((s) => s.scanId === created.scanId));
});

// The carriers SPA's flow: create a carrier, then load + replace it (typed).
test('carriers flow: create, get, update carrier (typed)', async () => {
  const created = await client.createCarrier(new CarrierWrite({
    carrierFullName: 'E2E Freight Lines', scac: 'E2EF', carrierTypeCode: 'LTL', status: 1,
  }));
  assert.ok(created.carrierId > 0);
  const got = await client.getCarrier(created.carrierId);
  assert.equal(got.carrierFullName, 'E2E Freight Lines');
  const updated = await client.updateCarrier(created.carrierId, new CarrierWrite({
    carrierFullName: 'E2E Freight Lines', scac: 'E2EF', carrierCity: 'Toledo', status: 0,
  }));
  assert.equal(updated.carrierCity, 'Toledo');
});

// The parts SPA's flow: create a part, then load + replace it (typed).
test('parts flow: create, get, update part (typed)', async () => {
  // Back-check widening: the full part_num schema round-trips (tolerances, trim,
  // tooling/line, skid/packaging, notes) — columns SQLite CI previously never saw.
  const created = await client.createPart(new PartWrite({
    customerId: 4001, enduserPartNum: 'E2E-PN', sheetType: 'SHEET', alloy: '3003', temper: 'H14', gauge: 0.05, itemStatus: 1,
    gaugeP: 0.002, gaugeM: 0.002, materialEndUse: 'Heat shield', theoreticalUnitWt: 1.2345,
    incomingCoilWidth: 48.5, trimmedCoilWidth: 46.5, trimTypeCode: 1, trimmingRequired: 'Y', shTolerancePlus: 3, shToleranceMinus: 2,
    dieId: 2001, lineNum: 110, spm: 80, autoparts: 1, specialPart: 'N',
    piecesSkid: 500, maxSkidWt: 4000, stacksSkid: 4, supplierCode: 77,
    packagingBands: '2 straps', itemDesc: 'E2E part desc', itemNote: 'E2E note',
  }));
  assert.ok(created.partNumId > 0);
  assert.equal(created.gaugeP, 0.002);
  assert.equal(created.materialEndUse, 'Heat shield');
  assert.equal(created.dieId, 2001);
  assert.equal(created.piecesSkid, 500);
  const got = await client.getPart(created.partNumId);
  assert.equal(got.enduserPartNum, 'E2E-PN');
  assert.equal(got.trimmingRequired, 'Y');
  assert.equal(got.lineNum, 110);
  assert.equal(got.itemNote, 'E2E note');
  const updated = await client.updatePart(created.partNumId, new PartWrite({
    customerId: 4001, enduserPartNum: 'E2E-PN', alloy: '5052', itemStatus: 0,
    dieId: 2002, lineNum: 120, maxSkidWt: 5000,
  }));
  assert.equal(updated.alloy, '5052');
  assert.equal(updated.dieId, 2002);
  assert.equal(updated.maxSkidWt, 5000);
});

// The customers SPA's flow: create a customer, load + replace it, add a contact (typed).
test('customers flow: create, get, update customer + add contact (typed)', async () => {
  const created = await client.createCustomer(new CustomerWrite({
    customerName: 'E2E Customer', customerShortName: 'E2E', customerState: 'OH',
  }));
  assert.ok(created.customerId > 0);
  const got = await client.getCustomer(created.customerId);
  assert.equal(got.customerName, 'E2E Customer');
  const updated = await client.updateCustomer(created.customerId, new CustomerWrite({
    customerName: 'E2E Customer', customerShortName: 'E2E2', customerCity: 'Toledo',
  }));
  assert.equal(updated.customerShortName, 'E2E2');
  const contact = await client.createCustomerContact(created.customerId, new CustomerContactWrite({
    lastName: 'Smith', firstName: 'Pat', department: 'QA',
  }));
  assert.ok(contact.contactId > 0);
  const contacts = await client.getCustomerContacts(created.customerId);
  assert.ok(contacts.some((k) => k.contactId === contact.contactId));
});

// The maintenance SPA's flow: create a log, then load + replace it (typed).
test('maintenance flow: create, get, update maint log (typed)', async () => {
  const created = await client.createMaintLog(new MaintLogWrite({
    maintLogStatus: 'Completed', probDateTime: new Date(), probDetails: 'E2E fault', author: 'e2e',
  }));
  assert.ok(created.maintLogId > 0);
  const got = await client.getMaintLog(created.maintLogId);
  assert.equal(got.maintLogId, created.maintLogId);
  const updated = await client.updateMaintLog(created.maintLogId, new MaintLogWrite({
    maintLogStatus: 'Completed', probDateTime: new Date(), probDetails: 'E2E fault', author: 'e2e', assignedTo: 'tech-2',
  }));
  assert.equal(updated.assignedTo, 'tech-2');
});

// The shipping SPA's flow: list shipments, open one, record dispatch (patch).
test('shipping flow: listShipments, getShipment, patch dispatch (typed)', async () => {
  const page = await client.listShipments(1, 10, undefined, undefined, undefined);
  assert.ok(Array.isArray(page.items));
  if (page.items.length > 0) {
    const id = page.items[0].packingList;
    const s = await client.getShipment(id);
    assert.equal(s.packingList, id);
    const updated = await client.patchShipment(id, new ShipmentStatusPatch({ shipmentNotes: 'E2E dispatch' }));
    assert.equal(updated.shipmentNotes, 'E2E dispatch');
  }
});

// The QA-results SPA's flow: posted + in-progress mechanical test results.
test('qa-results flow: listTestResults + listTempTestResults (typed)', async () => {
  const posted = await client.listTestResults(1, 10, undefined, undefined, undefined, undefined, undefined, undefined);
  assert.ok(Array.isArray(posted.items));
  // Back-check: a posted result carries its coil linkage (coil_abc_num + source_id),
  // the real pst_test_result composite-PK members that had been dropped.
  assert.ok(posted.items.length > 0);
  assert.ok(posted.items.every((r) => typeof r.coilAbcNum === 'number' && typeof r.sourceId === 'number'));
  assert.ok(posted.items.some((r) => r.coilAbcNum === 5001));
  const temp = await client.listTempTestResults(1, 10, undefined, undefined, undefined, undefined, undefined, undefined);
  assert.ok(Array.isArray(temp.items));
  // In-progress results carry coil_org_num (the coil match by org number).
  assert.ok(temp.items.every((r) => typeof r.coilOrgNum === 'string' && r.coilOrgNum.length > 0));
});

// The coil-inventory SPA's flow: weight rollup, processing history, inline patch.
test('coil-inventory flow: summary, processing, and patch (typed)', async () => {
  const groups = await client.coilInventorySummary('alloy');
  assert.ok(Array.isArray(groups));
  const proc = await client.getCoilProcessing(5001);
  assert.ok(Array.isArray(proc));
  const updated = await client.patchCoil(5001, new CoilPatch({ coilLocation: 'E2E-BAY' }));
  assert.equal(updated.coilLocation, 'E2E-BAY');
});

// The order-entry SPA's read flow: search → open an order's full detail.
test('order-entry read flow: listOrders then getOrderDetail (typed)', async () => {
  const page = await client.listOrders(1, 25, undefined, undefined, undefined, undefined);
  assert.ok(page.totalCount >= 1);
  assert.ok(page.items.length >= 1);
  const id = page.items[0].orderAbcNum;
  const detail = await client.getOrderDetail(id);
  assert.equal(detail.order.orderAbcNum, id);
  assert.ok(Array.isArray(detail.items));
});

// The Sales SPA's read flow: pending-quote list carries the joined customer/contact and
// the latest win probability; opening a quote returns the typed header.
test('sales flow: listQuotes carries latest probability, getQuote header (typed)', async () => {
  const quotes = await client.getSalesQuotes(undefined);
  assert.ok(quotes.length >= 3);
  const q1 = quotes.find((q) => q.quoteId === 7001 && q.quoteRevisionId === 1);
  assert.ok(q1, 'seeded quote 7001-1 present');
  assert.equal(q1.customerShortName !== undefined, true);
  assert.equal(q1.latestProbability, 65); // newest of the two seeded reviews
  const header = await client.getSalesQuote(7001, 1);
  assert.equal(header.alloy, '3003');
  assert.equal(header.approvalSales, 'Y');
});

// Sales search filters by customer / end-use / alloy.
test('sales flow: getSalesQuotes(search) filters (typed)', async () => {
  const hit = await client.getSalesQuotes('5052');
  assert.ok(hit.length >= 1);
  assert.ok(hit.every((q) => q.quoteId === 7002));
  const miss = await client.getSalesQuotes('nope-no-match');
  assert.equal(miss.length, 0);
});

// Sales contacts: the address book, optionally scoped to a customer.
test('sales flow: getSalesContacts all + by customer (typed)', async () => {
  const all = await client.getSalesContacts(undefined);
  assert.ok(all.length >= 3);
  const scoped = await client.getSalesContacts(4002);
  assert.ok(scoped.length >= 1);
  assert.ok(scoped.every((c) => c.customerId === 4002));
});

// The Sales CRM write flow: log a follow-up event, then record a probability review —
// the recorded review becomes the quote's new latest probability.
test('sales flow: createSalesReminder + createSalesProbability (typed)', async () => {
  const events0 = await client.getSalesReminders(7002, 2);
  const ev = await client.createSalesReminder(7002, 2, new SalesReminderWrite({
    eventNotes: 'E2E follow-up', eventStatus: 'OPEN', userId: 'e2e',
  }));
  assert.ok(ev.eventId > 0);
  assert.equal(ev.eventStatus, 'OPEN');
  const events1 = await client.getSalesReminders(7002, 2);
  assert.equal(events1.length, events0.length + 1);

  const review = await client.createSalesProbability(7002, 2, new SalesProbabilityWrite({
    salesProbabilityPercent: 90, probabilityNote: 'E2E very likely',
  }));
  assert.equal(review.salesProbabilityPercent, 90);
  const quotes = await client.getSalesQuotes(undefined);
  const q = quotes.find((x) => x.quoteId === 7002 && x.quoteRevisionId === 2);
  assert.equal(q.latestProbability, 90); // the just-recorded review is now newest
});

// Probability validation: percent must be 0–100. The generated client surfaces a
// declared 400 validation body by throwing the typed HttpValidationProblemDetails.
test('sales flow: createSalesProbability rejects out-of-range percent (typed)', async () => {
  await assert.rejects(
    () => client.createSalesProbability(7001, 1, new SalesProbabilityWrite({ salesProbabilityPercent: 150 })),
    (err) => err?.status === 400 && !!err?.errors?.salesProbabilityPercent,
  );
});

// The coil-ownership transfer ledger: the seeded historical transfer joins orig/new
// customer names and the coil's metal details (typed, read-only).
test('coil-ownership flow: ledger carries owner change + coil details (typed)', async () => {
  const ledger = await client.getCoilOwnershipTransfers(undefined);
  assert.ok(ledger.length >= 1);
  const seeded = ledger.find((t) => t.certificateNum === 8001);
  assert.ok(seeded, 'seeded certificate 8001 present');
  assert.equal(seeded.coilAbcNumOrig, 5001);
  assert.equal(seeded.customerShortNameOrig, 'ACME');
  assert.equal(seeded.customerShortNameNew, 'BETA');
  assert.equal(seeded.coilAlloy2, '3003');
});

// The printable certificate joins full orig/new customer addresses + coil details.
test('coil-ownership flow: certificate has full addresses (typed)', async () => {
  const cert = await client.getCoilOwnershipTransferCertificate(8001);
  assert.equal(cert.customerFullNameOrig, 'ACME METALS');
  assert.equal(cert.customerCityOrig, 'Detroit');
  assert.equal(cert.customerFullNameNew, 'BETA FAB');
  assert.equal(cert.customerCityNew, 'Cleveland');
  assert.equal(cert.coilTemper, 'H14');
});

// Missing certificate → typed ApiException(404).
test('coil-ownership flow: certificate(missing) throws ApiException(404) (typed)', async () => {
  await assert.rejects(
    () => client.getCoilOwnershipTransferCertificate(999999),
    (e) => e instanceof ApiException && e.status === 404,
  );
});

// The transferable-coil picker, optionally scoped to a customer.
test('coil-ownership flow: transferable coils picker (typed)', async () => {
  const all = await client.getTransferableCoils(undefined, undefined);
  assert.ok(all.length >= 3);   // 5001/5002/5003 have balance > 0; 5004 is fully consumed (excluded)
  const beta = await client.getTransferableCoils(4002, undefined);
  assert.ok(beta.length >= 1);
  assert.ok(beta.every((c) => c.customerId === 4002));
});

// The transfer write flow: record a transfer; it issues the next certificate and MINTS a new
// coil for the new owner (the source coil is marked Transferred, its own owner preserved).
test('coil-ownership flow: createTransfer issues cert + mints a new coil (typed)', async () => {
  // coil 5002 is owned by ACME (4001); transfer it to BETA (4002).
  const before = await client.getCoil(5002);
  assert.equal(before.customerId, 4001);

  const created = await client.createCoilOwnershipTransfer(new CoilOwnershipTransferWrite({
    coilAbcNumOrig: 5002, customerIdNew: 4002, transferPerformedBy: 'e2e',
    authorizationNote: 'E2E auth', notes: 'E2E transfer',
  }));
  assert.ok(created.certificateNum > 8001); // server-assigned, after the seed
  assert.equal(created.customerIdOrig, 4001);
  assert.equal(created.customerIdNew, 4002);

  // the source coil is Transferred (13) and keeps its own owner (audit trail preserved, not re-pointed)
  const after = await client.getCoil(5002);
  assert.equal(after.coilStatus, 13);
  assert.equal(after.customerId, 4001);

  // a new coil was minted for the new owner: New (2), owned by BETA, from ACME
  assert.ok(created.coilAbcNumNew > 0 && created.coilAbcNumNew !== 5002);
  const minted = await client.getCoil(created.coilAbcNumNew);
  assert.equal(minted.customerId, 4002);
  assert.equal(minted.coilFromCustId, 4001);
  assert.equal(minted.coilStatus, 2);

  // and the certificate is retrievable with both customers resolved
  const cert = await client.getCoilOwnershipTransferCertificate(created.certificateNum);
  assert.equal(cert.customerShortNameOrig, 'ACME');
  assert.equal(cert.customerShortNameNew, 'BETA');
});

// Transfer of a non-existent coil → 404.
test('coil-ownership flow: transfer of missing coil → 404 (typed)', async () => {
  await assert.rejects(
    () => client.createCoilOwnershipTransfer(new CoilOwnershipTransferWrite({ coilAbcNumOrig: 888888, customerIdNew: 4002 })),
    (e) => e instanceof ApiException && e.status === 404,
  );
});

// Transfer validation: coil + new owner are required.
test('coil-ownership flow: createTransfer requires coil + new owner (typed)', async () => {
  await assert.rejects(
    () => client.createCoilOwnershipTransfer(new CoilOwnershipTransferWrite({ notes: 'missing required' })),
    (err) => err?.status === 400 && !!err?.errors?.coilAbcNumOrig && !!err?.errors?.customerIdNew,
  );
});

// The Security/authorization flow: the load-bearing effective-permission resolution
// (MAX of direct + group grants), the admin reads, and the grant/membership writes.
test('security flow: effective permissions = MAX(direct, group) (typed)', async () => {
  // Seeded: jsmith(9001) in Operators(10). Operators grant: Order Entry=ReadOnly(0),
  // Inventory(Coil)=Write(1). jsmith ALSO has a DIRECT Order Entry=Write(1) grant.
  const perms = await client.getUserEffectivePermissions(9001);
  const oe = perms.find((p) => p.applicationName === 'Order Entry');
  const ci = perms.find((p) => p.applicationName === 'Inventory(Coil)');
  assert.ok(oe && ci, 'both granted features present');
  assert.equal(oe.privilege, 1);          // MAX(direct 1, group 0) = 1
  assert.equal(oe.viaGroup, false);       // the direct grant tied the max
  assert.equal(oe.privilegeLabel, 'Write');
  assert.equal(ci.privilege, 1);          // group-only
  assert.equal(ci.viaGroup, true);
  // mlee(9002) in Admins(11): only User Control=Write.
  const adminPerms = await client.getUserEffectivePermissions(9002);
  assert.ok(adminPerms.some((p) => p.applicationName === 'User Control' && p.privilege === 1 && p.viaGroup === true));
});

test('security flow: admin reads — users, groups, applications, user groups (typed)', async () => {
  const users = await client.getSecurityUsers();
  assert.ok(users.some((u) => u.loginId === 'jsmith'));
  const groups = await client.getSecurityGroups();
  assert.ok(groups.some((g) => g.groupName === 'Operators'));
  const apps = await client.getSecurityApplications();
  assert.ok(apps.some((a) => a.applicationName === 'User Control'));
  const jsmithGroups = await client.getUserGroups(9001);
  assert.ok(jsmithGroups.some((g) => g.userGroupId === 10));
});

test('security flow: create user/group/app, grant + membership change perms (typed)', async () => {
  const u = await client.createSecurityUser(new SecurityUserWrite({ loginId: 'e2euser', userFirstName: 'E2E', userLastName: 'User', userStatus: 1 }));
  assert.ok(u.userId > 0);
  const g = await client.createSecurityGroup(new SecurityGroupWrite({ groupName: 'E2E-Group' }));
  const a = await client.createSecurityApplication(new SecurityApplicationWrite({ applicationName: 'E2E-Feature' }));

  // New user has no permissions yet.
  let perms = await client.getUserEffectivePermissions(u.userId);
  assert.equal(perms.length, 0);

  // Grant the group Write on the feature, then add the user to the group → user inherits it.
  await client.setGroupApplicationGrant(g.userGroupId, a.applicationId, new GrantWrite({ privilege: 1 }));
  await client.addUserToGroup(u.userId, g.userGroupId);
  perms = await client.getUserEffectivePermissions(u.userId);
  const feat = perms.find((p) => p.applicationName === 'E2E-Feature');
  assert.ok(feat && feat.privilege === 1 && feat.viaGroup === true);

  // A direct ReadOnly grant does NOT lower the effective Write (MAX wins).
  await client.setUserApplicationGrant(u.userId, a.applicationId, new GrantWrite({ privilege: 0 }));
  perms = await client.getUserEffectivePermissions(u.userId);
  assert.equal(perms.find((p) => p.applicationName === 'E2E-Feature').privilege, 1);

  // Remove from group → only the direct ReadOnly remains.
  await client.removeUserFromGroup(u.userId, g.userGroupId);
  perms = await client.getUserEffectivePermissions(u.userId);
  const after = perms.find((p) => p.applicationName === 'E2E-Feature');
  assert.ok(after && after.privilege === 0 && after.viaGroup === false);
});

// Grant against a missing user/application → 404.
test('security flow: grant to missing user → 404 (typed)', async () => {
  await assert.rejects(
    () => client.setUserApplicationGrant(888888, 1, new GrantWrite({ privilege: 1 })),
    (e) => e instanceof ApiException && e.status === 404,
  );
});

// Receiving BOL line items (legacy coil_receiving): the header+lines aggregate, adding
// coil lines (coil_id auto-assigned 1..n), the NOT-NULL coil_org_num guard, and delete.
test('receiving flow: BOL coil line items add/list/detail/delete (typed)', async () => {
  // Seeded BOL 5501 has two coils.
  const seeded = await client.getReceivingBolCoils(5501);
  assert.ok(seeded.length >= 2);
  assert.ok(seeded.every((c) => typeof c.coilOrgNum === 'string'));
  const detail = await client.getReceivingBolDetail(5501);
  assert.equal(detail.bol.receivingBolId, 5501);
  assert.equal(detail.coils.length, seeded.length);

  // Create a fresh BOL, add two coils — coil_id auto-increments within the BOL.
  const bol = await client.createReceivingBol(new ReceivingBolWrite({ bol: 'E2E-COILBOL', customerId: 4001, createdBy: 'e2e', status: 0 }));
  const c1 = await client.addReceivingBolCoil(bol.receivingBolId, new ReceivingBolCoilWrite({ coilOrgNum: 'E2E-ORG-1', alloy: '3003', netWeight: 9000 }));
  const c2 = await client.addReceivingBolCoil(bol.receivingBolId, new ReceivingBolCoilWrite({ coilOrgNum: 'E2E-ORG-2', alloy: '5052', netWeight: 8000 }));
  assert.equal(c1.coilId, 1);
  assert.equal(c2.coilId, 2);
  assert.equal(c2.coilOrgNum, 'E2E-ORG-2');

  let list = await client.getReceivingBolCoils(bol.receivingBolId);
  assert.equal(list.length, 2);

  // coil_org_num is required (NOT NULL).
  await assert.rejects(
    () => client.addReceivingBolCoil(bol.receivingBolId, new ReceivingBolCoilWrite({ alloy: '3003' })),
    (err) => err?.status === 400 && !!err?.errors?.coilOrgNum,
  );
  // Adding to a missing BOL → 404.
  await assert.rejects(
    () => client.addReceivingBolCoil(987654, new ReceivingBolCoilWrite({ coilOrgNum: 'X' })),
    (e) => e instanceof ApiException && e.status === 404,
  );

  // Delete one line.
  await client.deleteReceivingBolCoil(bol.receivingBolId, 1);
  list = await client.getReceivingBolCoils(bol.receivingBolId);
  assert.equal(list.length, 1);
  assert.equal(list[0].coilId, 2);
});

// Production reporting (legacy daily_prod / silverdome3 w_report_production_*): the
// new report aggregations over the seeded jobs/coils/downtime/lines.
test('reporting flow: line efficiency carries downtime (typed)', async () => {
  const rows = await client.getLineEfficiency(undefined, undefined);
  assert.ok(rows.length > 0);
  const l110 = rows.find((r) => r.lineNum === 110);
  assert.ok(l110, 'line 110 present');
  assert.ok(l110.jobCount >= 2);
  // Seeded downtime: line 110 has two events (20 + 5 min) = 25 min.
  assert.equal(l110.downtimeEvents, 2);
  assert.equal(Math.round(l110.downtimeMinutes), 25);
});

test('reporting flow: monthly production buckets by YYYY-MM (typed)', async () => {
  const rows = await client.getMonthlyProduction(undefined, undefined);
  assert.ok(rows.length > 0);
  assert.ok(rows.every((r) => /^\d{4}-\d{2}$/.test(r.month)));
  assert.ok(rows.every((r) => typeof r.processedWt === 'number'));
});

test('reporting flow: downtime events carry computed duration (typed)', async () => {
  const rows = await client.getProductionDowntime(undefined, undefined, undefined);
  assert.ok(rows.length >= 3);
  // Closed events have a non-negative numeric duration; open events (no end) are null.
  assert.ok(rows.every((r) => r.durationMinutes == null || (typeof r.durationMinutes === 'number' && r.durationMinutes >= 0)));
  const line110 = await client.getProductionDowntime(undefined, undefined, 110);
  assert.ok(line110.length >= 2);
  assert.ok(line110.every((r) => r.lineNum === 110));
  // The seeded line-110 events are closed → their durations sum to ~25 min.
  assert.ok(line110.filter((r) => r.durationMinutes != null).length >= 2);
});

test('reporting flow: on-time delivery (typed)', async () => {
  const rows = await client.getOnTimeDelivery(undefined, undefined);
  // Seeded: only job 1003 (line 120) is finished, due d+5 finished d+3 → on time.
  const l120 = rows.find((r) => r.lineNum === 120);
  assert.ok(l120, 'line 120 has a finished job');
  assert.equal(l120.finishedJobs, 1);
  assert.equal(l120.onTime, 1);
  assert.equal(l120.late, 0);
  assert.equal(l120.onTimePct, 100);
});

// Customer / shipment reporting (legacy silverdome3 w_report_customer_*).
test('reporting flow: customer shipments + open shipments (typed)', async () => {
  const cs = await client.getCustomerShipments(undefined, undefined);
  assert.ok(cs.length >= 1);
  // Seeded: cust 4001 has 1 shipped (8801), cust 4002 has 1 open (8802).
  const acme = cs.find((r) => r.customerId === 4001);
  assert.ok(acme && acme.shipped === 1);
  const beta = cs.find((r) => r.customerId === 4002);
  assert.ok(beta && beta.open === 1);

  const open = await client.getOpenShipments();
  assert.ok(open.some((r) => r.packingList === 8802));
  assert.ok(open.every((r) => r.packingList !== 8801)); // shipped one excluded
});

test('reporting flow: customer orders + skid counts (typed)', async () => {
  const orders = await client.getCustomerOrdersReport(undefined);
  assert.ok(orders.length >= 1);
  assert.ok(orders.every((r) => typeof r.orderAbcNum === 'number'));
  const scoped = await client.getCustomerOrdersReport(4001);
  assert.ok(scoped.length >= 1 && scoped.every((r) => r.customerId === 4001));

  const skids = await client.getCustomerSkidCount();
  assert.ok(Array.isArray(skids));
  assert.ok(skids.every((r) => typeof r.skidCount === 'number'));
});

// Inventory reporting (legacy silverdome3 w_report_inv_*).
test('reporting flow: inventory — coil by alloy, on-hold, skid, unmatched (typed)', async () => {
  const byAlloy = await client.getCoilInventory(undefined);
  assert.ok(byAlloy.some((r) => r.coilAlloy2 === '3003' && r.coilCount >= 2));
  assert.ok(byAlloy.every((r) => typeof r.totalBalance === 'number'));

  // Seeded: coil 5004 is on hold (status 3).
  const onHold = await client.getOnHoldCoils();
  assert.ok(onHold.some((r) => r.coilAbcNum === 5004));
  assert.ok(onHold.every((r) => r.coilStatus === 3));

  const skids = await client.getSkidInventory();
  assert.ok(skids.length >= 1 && skids.every((r) => typeof r.skidCount === 'number'));

  // Coil 5004 is on no job (not in process_coil); processed coils are excluded.
  const unmatched = await client.getUnmatchedCoils();
  assert.ok(unmatched.some((r) => r.coilAbcNum === 5004));
  assert.ok(unmatched.every((r) => r.coilAbcNum !== 5001));
});

// QA / scrap reporting (legacy silverdome3 w_report_qa, w_report_scrap).
test('reporting flow: QA mechanical + scrap summaries (typed)', async () => {
  const qa = await client.getQaMechanical(undefined, undefined);
  assert.ok(qa.length >= 1);
  assert.ok(qa.every((r) => typeof r.resultCount === 'number'));
  assert.ok(qa.every((r) => r.avgYts == null || typeof r.avgYts === 'number'));

  // Seeded scrap: type 1 (DENT) on job 1001, type 2 (SCR) on job 1003.
  const byType = await client.getScrapSummary();
  const dent = byType.find((r) => r.scrapType === 1);
  assert.ok(dent && dent.scrapCode === 'DENT' && dent.skidCount >= 1);

  const byJob = await client.getScrapByJob();
  assert.ok(byJob.some((r) => r.scrapAbJobNum === '1001'));
  assert.ok(byJob.every((r) => typeof r.totalNetWt === 'number'));
});

// A client that authenticates as a specific ABIS user (X-User-Login) in addition to the
// API key — exercises the server-side per-feature enforcement (legacy f_security_door).
function loginClient(login) {
  return new AbisClient(base, {
    fetch: (url, init = {}) => {
      const headers = new Headers(init.headers);
      headers.set('X-Api-Key', key);
      headers.set('X-User-Login', login);
      return fetch(url, { ...init, headers });
    },
  });
}

// Server-side enforcement: the "User Control" gate on security-admin writes.
test('security enforcement: non-admin is denied, admin allowed, service account bypasses (typed)', async () => {
  // jsmith (Operators) holds Order Entry but NOT User Control → creating a group is 403.
  await assert.rejects(
    () => loginClient('jsmith').createSecurityGroup(new SecurityGroupWrite({ groupName: 'X-by-jsmith' })),
    (e) => e instanceof ApiException && e.status === 403,
  );
  // mlee (Admins) holds User Control (Write) → allowed.
  const g = await loginClient('mlee').createSecurityGroup(new SecurityGroupWrite({ groupName: 'X-by-mlee' }));
  assert.ok(g.userGroupId > 0);
  // The API-key service account (no X-User-Login) bypasses the gate.
  const g2 = await client.createSecurityGroup(new SecurityGroupWrite({ groupName: 'X-by-service' }));
  assert.ok(g2.userGroupId > 0);
});

// /security/me/* resolves the caller and drives UI enable/read-only decisions.
test('security enforcement: me/permissions + me/allowed (typed)', async () => {
  const mine = await loginClient('jsmith').getMyPermissions();
  assert.ok(mine.some((p) => p.applicationName === 'Order Entry'));
  // jsmith can write Order Entry but not User Control.
  const oe = await loginClient('jsmith').getMyAllowed('Order Entry', 1);
  assert.equal(oe.allowed, true);
  const uc = await loginClient('jsmith').getMyAllowed('User Control', 1);
  assert.equal(uc.allowed, false);
  // service account: empty permissions, but allowed (bypass) everywhere.
  const svc = await client.getMyPermissions();
  assert.equal(svc.length, 0);
  const svcAllowed = await client.getMyAllowed('User Control', 1);
  assert.equal(svcAllowed.allowed, true);
});

// Coil-minting at receiving (legacy w_coil_receiving save): mint COIL inventory for a
// BOL's lines, then generate the 861 (Receiving Advice).
test('receiving flow: mint coil inventory + real 861 (typed)', async () => {
  const bol = await client.createReceivingBol(new ReceivingBolWrite({ bol: 'E2E-MINT', customerId: 4001, createdBy: 'e2e', status: 0 }));
  const good = await client.addReceivingBolCoil(bol.receivingBolId, new ReceivingBolCoilWrite({ coilOrgNum: 'MINT-1', alloy: '3003', temper: 'H14', netWeight: 9000, coilGauge: 0.05, coilWidth: 48 }));
  const dmg = await client.addReceivingBolCoil(bol.receivingBolId, new ReceivingBolCoilWrite({ coilOrgNum: 'MINT-2', alloy: '5052', netWeight: 8000, damagedFault: 1 }));
  assert.ok(good.coilAbcNum == null); // not minted yet (null)

  const r = await client.mintBolCoils(bol.receivingBolId);
  assert.equal(r.minted, 2);
  const lines = r.coils;
  const l1 = lines.find((c) => c.coilId === good.coilId);
  const l2 = lines.find((c) => c.coilId === dmg.coilId);
  assert.ok(l1.coilAbcNum > 0 && l2.coilAbcNum > 0);

  // The minted coils now exist in inventory; damaged → status 11 (on-hold), good → 2 (new).
  const c1 = await client.getCoil(l1.coilAbcNum);
  assert.equal(c1.coilStatus, 2);
  assert.equal(c1.customerId, 4001);
  assert.equal(c1.coilOrgNum, 'MINT-1');
  const c2 = await client.getCoil(l2.coilAbcNum);
  assert.equal(c2.coilStatus, 11);

  // Idempotent: re-mint creates nothing new.
  const again = await client.mintBolCoils(bol.receivingBolId);
  assert.equal(again.minted, 0);

  // 861: customer 4001 isn't a configured trading partner -> the generator rejects (422).
  await assert.rejects(() => client.generateReceiving861(bol.receivingBolId));

  // The seeded Novelis BOL (5500, customer 1153) IS a configured 861 partner -> build + store
  // the X12, never transmit.
  const edi = await client.generateReceiving861(5500);
  assert.equal(edi.status, 'generated');
  assert.equal(edi.partner, 'Novelis');
  assert.equal(edi.transmitted, false);
  assert.ok(edi.ediFileId > 0);
});

// EDI 870 (Order/Coil Status): batch every unsent Aleris item + finished-job scrap into one X12 — built and
// stored, never transmitted. Non-partner customers are rejected; a second run has nothing new to report.
test('edi flow: generate the Aleris 870 (typed, stored not transmitted)', async () => {
  await assert.rejects(() => client.generateEdi870(2582));    // Novelis 2582 has only an 861 profile → 422
  const nov = await client.generateEdi870(1153);              // Novelis 1153 IS a configured 870 partner
  assert.equal(nov.status, 'nothing');                        // wired, but no unsent items in the seed
  const r = await client.generateEdi870(1980);
  assert.equal(r.status, 'generated');
  assert.equal(r.partner, 'Aleris');
  assert.equal(r.transmitted, false);
  assert.ok(r.ediFileId > 0 && r.itemCount >= 1);
  const again = await client.generateEdi870(1980);
  assert.equal(again.status, 'nothing');   // report-once
});

// EDI 856 (ASN): generate from a shipment's packing list — built + stored, never transmitted.
test('edi flow: 856 rejects unknown + non-partner shipments (typed)', async () => {
  await assert.rejects(() => client.generateEdi856(999999));   // no shipment for this packing list -> 404
  await assert.rejects(() => client.generateEdi856(8801));     // shipment exists (cust 4001) but not an 856 partner -> 422
});

// Coil evaluation / QC (legacy coil_eval w_qc_sheet): QC coil picker, dimensional checks,
// and eval scrap (upsert on the composite key).
test('coil-eval flow: QC coils, dimension checks, eval scrap (typed)', async () => {
  const coils = await client.getQcCoils(1001);
  assert.ok(coils.length >= 1 && coils.every((c) => c.abJobNum === 1001));

  // Seeded skid 3001 has two dimension checks (one out of spec).
  const checks = await client.getDimensionChecks(3001);
  assert.ok(checks.length >= 2);
  assert.ok(checks.some((c) => c.inSpec === 0));

  const created = await client.createDimensionCheck(3001, new DimensionCheckWrite({
    pcNumber: 99, gauge: 0.125, width: 48.5, lengthOper: 96, lengthDrive: 96.0, square: 0.01, inSpec: 1, checkedBy: 'e2e',
  }));
  assert.ok(created.dimensionCheckNum > 0);
  assert.equal(created.inSpec, 1);

  // Seeded eval scrap on coil 5001 / job 1001 / type 1.
  const scrap = await client.getEvalScrap(1001);
  assert.ok(scrap.some((s) => s.coilAbcNum === 5001 && s.scrapItemType === 1));

  // Upsert: same composite key updates rather than duplicating.
  const up = await client.upsertEvalScrap(new EvalScrapWrite({
    coilAbcNum: 5001, abJobNum: 1001, scrapItemType: 1, scrapItemPiece: 9, scrapItemNetWt: 200, scrapItemNote: 'updated',
  }));
  assert.equal(up.scrapItemPiece, 9);
  const after = await client.getEvalScrap(1001);
  assert.equal(after.filter((s) => s.coilAbcNum === 5001 && s.scrapItemType === 1).length, 1); // still one row

  // A new scrap type adds a row.
  await client.upsertEvalScrap(new EvalScrapWrite({ coilAbcNum: 5001, abJobNum: 1001, scrapItemType: 2, scrapItemNetWt: 50 }));
  const after2 = await client.getEvalScrap(1001);
  assert.ok(after2.some((s) => s.scrapItemType === 2));

  // Required-field validation.
  await assert.rejects(
    () => client.upsertEvalScrap(new EvalScrapWrite({ scrapItemNetWt: 10 })),
    (err) => err?.status === 400,
  );
});

// Production folder (legacy prod-folder w_production_folder): the job packet summary +
// e-folder notes (with author name) and adding a note.
test('prod-folder flow: folder summary + e-folder notes (typed)', async () => {
  const f = await client.getProductionFolder(1001);
  assert.equal(f.abJobNum, 1001);
  assert.ok(f.coilCount >= 1 && f.skidCount >= 1 && f.noteCount >= 2);
  assert.ok(typeof f.customerShortName === 'string');

  const notes = await client.getJobFolderNotes(1001);
  assert.ok(notes.length >= 2);
  assert.ok(notes.every((n) => typeof n.userName === 'string' && n.userName.length > 0));

  const added = await client.addJobFolderNote(1001, new JobFolderNoteWrite({ userId: 9001, notes: 'E2E folder note' }));
  assert.equal(added.notes, 'E2E folder note');
  assert.equal(added.userId, 9001);
  const after = await client.getJobFolderNotes(1001);
  assert.equal(after.length, notes.length + 1);

  // userId required when no authenticated user.
  await assert.rejects(
    () => client.addJobFolderNote(1001, new JobFolderNoteWrite({ notes: 'no user' })),
    (err) => err?.status === 400,
  );

  const missing = await client.getProductionFolder(999999).catch((e) => e);
  assert.ok(missing instanceof ApiException && missing.status === 404);
});

// Stacker line board + error log (legacy stacker_110).
test('stacker flow: line board + error log (typed)', async () => {
  const board = await client.getStackerBoard(110);
  assert.ok(board.length >= 1 && board.every((j) => j.lineNum === 110));
  assert.ok(board.every((j) => typeof j.coilCount === 'number' && typeof j.skidCount === 'number'));

  // Seeded errors: 9701 on line 110 (Jam), 9702 on line 120 (PLC fault).
  const errs110 = await client.getLineErrors(110, undefined, undefined);
  assert.ok(errs110.some((e) => e.errorEvtId === 9701 && e.errorType === 'Jam'));
  assert.ok(errs110.every((e) => e.lineId === 110));

  const created = await client.createLineError(new LineErrorWrite({
    errorTypeId: 3, errorUser: 'e2e', lineId: 110, abJobNum: 1001, title: 'E2E fault', errorComment: 'test',
  }));
  assert.ok(created.errorEvtId > 0);
  assert.equal(created.errorType, 'Operator');

  // Required fields.
  await assert.rejects(
    () => client.createLineError(new LineErrorWrite({ title: 'no type/user' })),
    (err) => err?.status === 400,
  );
});

test('pm flow: due board, definition CRUD, checklist, completion advance (typed)', async () => {
  // ---- due board: seeded 7001 is overdue, 7002 due in 3 days, 7003 is 90 days out,
  // 7004 is retired (pm_status 0) and must never appear however wide the window.
  const due = await client.getPmsDue(7, undefined);
  const overdue = due.find((p) => p.pmId === 7001);
  assert.ok(overdue, 'seeded overdue PM 7001 should be on the 7-day board');
  assert.equal(overdue.dueBucket, 'overdue');
  assert.ok(overdue.daysUntilDue < 0);
  assert.equal(overdue.systemEquipment, 'Blanking line BL110');   // hierarchy joined
  assert.equal(overdue.itemDevice, 'Mandrel bearing');
  assert.ok(due.some((p) => p.pmId === 7002 && p.dueBucket === 'due'));
  assert.ok(!due.some((p) => p.pmId === 7003), '90-day PM is outside a 7-day window');
  const wide = await client.getPmsDue(3650, undefined);
  assert.ok(!wide.some((p) => p.pmId === 7004), 'retired PM must never reach the due board');
  // Most-overdue-first ordering.
  const days = due.map((p) => p.daysUntilDue);
  assert.deepEqual(days, [...days].sort((a, b) => a - b));

  // ---- create / read / update
  const created = await client.createPm(new PmWrite({
    pmNotice: 'E2E gearbox oil', maintFreq: '4XY', sysEquipmentId: 300, subsysEquipmentId: 400,
    itemDeviceId: 500, titleCraftId: 600, groupDepartmentId: 10, assignedToGroup: 'Maintenance',
    pmStatus: 1, daysBetween: 30, nextDueDate: new Date(),
  }));
  assert.ok(created.pmId > 0);
  assert.equal(created.subsystemEquipment, 'Uncoiler');
  const got = await client.getPm(created.pmId);
  assert.equal(got.pmNotice, 'E2E gearbox oil');
  const updated = await client.updatePm(created.pmId, new PmWrite({
    pmNotice: 'E2E gearbox oil + filter', maintFreq: '4XY', pmStatus: 1, daysBetween: 30,
  }));
  assert.equal(updated.pmNotice, 'E2E gearbox oil + filter');

  // A bad equipment reference is a 400 (pre-checked), not an FK 500.
  await assert.rejects(
    () => client.createPm(new PmWrite({ pmNotice: 'bad ref', sysEquipmentId: 999999 })),
    (err) => err?.status === 400,
  );
  // pmNotice is required.
  await assert.rejects(() => client.createPm(new PmWrite({})), (err) => err?.status === 400);

  // ---- checklist: add, list, and the pm-scoped delete
  const action = await client.addPmAction(created.pmId, new PmActionWrite({
    actionItems: 'Drain oil', itemDetails: 'Catch pan under gearbox',
  }));
  assert.ok((await client.getPmActions(created.pmId)).some((a) => a.pmActionId === action.pmActionId));
  // Deleting through a DIFFERENT pm must not remove it.
  await assert.rejects(
    () => client.deletePmAction(7001, action.pmActionId),
    (err) => err?.status === 404,
  );
  await client.deletePmAction(created.pmId, action.pmActionId);
  assert.equal((await client.getPmActions(created.pmId)).length, 0);

  // ---- completion advances the schedule by daysBetween and records history
  const before = await client.getPm(created.pmId);
  const done = await client.completePm(created.pmId, new PmCompleteWrite({
    completedBy: 'e2e', completedNotes: 'ran it',
  }));
  assert.equal(done.advanceBasis, 'daysBetween');
  const expected = new Date(); expected.setDate(expected.getDate() + 30);
  assert.equal(new Date(done.nextDueDate).toDateString(), expected.toDateString());
  assert.notEqual(new Date(done.nextDueDate).toDateString(), new Date(before.nextDueDate).toDateString());
  const history = await client.getPmCompletions(created.pmId);
  assert.equal(history.length, 1);
  assert.equal(history[0].completedBy, 'e2e');

  // completedBy is required (the column is NOT NULL, and '' is NULL on Oracle).
  await assert.rejects(
    () => client.completePm(created.pmId, new PmCompleteWrite({})),
    (err) => err?.status === 400,
  );

  // ---- guarded delete: refused now that a completion exists
  await assert.rejects(() => client.deletePm(created.pmId), (err) => err?.status === 409);
  // Retiring is the documented alternative, and it drops off the due board.
  await client.updatePm(created.pmId, new PmWrite({ pmNotice: 'E2E retired', pmStatus: 0 }));
  const after = await client.getPmsDue(3650, undefined);
  assert.ok(!after.some((p) => p.pmId === created.pmId), 'retired PM leaves the due board');

  // A PM with no completions IS deletable, and its checklist goes with it.
  const spare = await client.createPm(new PmWrite({ pmNotice: 'E2E spare', pmStatus: 1 }));
  await client.addPmAction(spare.pmId, new PmActionWrite({ actionItems: 'x' }));
  await client.deletePm(spare.pmId);
  await assert.rejects(() => client.getPm(spare.pmId), (err) => err?.status === 404);
});
