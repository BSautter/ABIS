  CREATE OR REPLACE PROCEDURE "DBO"."P_846_CLEVELAND_CLIFF_CCSC"
is

--return number is

  edi_file_prefix		VARCHAR2(50) := 's_cliffs_ccsc_846_';
  ls_duns			customer.customer_duns_number%TYPE;
  ls_coil_classification	varchar2(3);
  --ls_coil_classification_qas	  varchar2(3);
  --li_skid_status_albl 	  sheet_skid.skid_sheet_status%TYPE;
  --ls_skid_status_cliff	  varchar2(3);
  --ls_skid_prod_desc_code	  varchar2(3);
  --li_scrap_status_albl	  sheet_skid.skid_sheet_status%TYPE;
  --ls_scrap_status_cliff	  varchar2(3);
  li_coil_status		coil.coil_status%TYPE;
  ls_coil_status_cliff		varchar2(3);
  ll_coil_length		number(8);
  ls_duns_cliff 		varchar2(32);
  ls_duns_albl			varchar2(32);
  ll_long_part_length		number(16,5);
  ll_scrap_skid_num		scrap_skid.scrap_skid_num%TYPE;
  --li_scrap_type_albl		  scrap_skid.scrap_type%TYPE;
  --ls_scrap_type_cliff 	  varchar2(3);
  --ll_coil_od			  number(8);
  --ll_coil_id			  number(8);
  ls_coil_od			char(9);
  ls_coil_id			char(9);

  li_gs_log			INTEGER;
  li_st_log			INTEGER;
  ls_today_short		VARCHAR2(20);
  ls_today			VARCHAR2(20);
  ls_now			VARCHAR2(20);
  li_coil_count 		int := 0;
  --ls_coil_count_string	  varchar2(32);
  li_skid_count 		int := 0;
  li_scrap_skid_count		int := 0;
  li_edi_846_id 		int;
  ls_vendor_item_num		varchar2(4);
  ls_coil_process_date		char(12);
  ls_qa_status_date		char(12);
  ls_coil_org_num		coil.coil_org_num%TYPE;
  ll_coil_abc_num		coil.coil_abc_num%TYPE;
  li_count			number;
  ls_production_desc_code	coil.production_desc_code%TYPE;    --TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
  --ls_production_desc_code	  varchar2(12); --TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
  ls_table67_mat_class_skid	abis_x12_skid.table67_material_class%TYPE;
  ls_table70_mat_status_skid	abis_x12_skid.table70_material_status_op%TYPE;
  ls_table67_mat_class_coil	abis_x12_coil.table67_material_class%TYPE;
  ls_table70_mat_status_coil	abis_x12_coil.table70_material_status_op%TYPE;
  ls_table67_mat_class_scrap	abis_scrap_type_x12.table67_material_class%TYPE;
  ls_table70_mat_status_scrap	abis_scrap_status_x12.table70_material_status_op%TYPE;
  ls_lot_num			coil.lot_num%TYPE;
  --li_skid_count_string	  varchar(32);
  --li_scrap_skid_count_string	  varchar(32);
  li_item_count 		number(6);
  ls_item_count_string		varchar(32);

  cursor skid_cursor is
  select production_sheet_item.prod_item_num,
	 sheet_skid.sheet_skid_num,
	 production_sheet_item.prod_item_net_wt,
	 production_sheet_item.prod_item_pieces,
	 sheet_skid.skid_sheet_status,
	 sheet_skid.skid_pieces,
	 sheet_skid.skid_date,
	 (sheet_skid.sheet_net_wt + sheet_skid.sheet_tare_wt) skid_gross_wt,
	 coil.coil_abc_num,
	 coil.coil_org_num,
	 coil.coil_gauge,
	 coil.coil_width,
	 NVL(coil.coil_mid_num, 'NA') coil_mid_num,
	 NVL(coil.part_num, 'NA') part_num,
	 NVL(coil.supplier_sales_num, 'NA') supplier_sales_num,
	 NVL(coil.purchase_order_num, 'NA') purchase_order_num,
	 NVL(coil.lot_num, 'NA') lot_num,

	 --nvl(coil.production_desc_code, 'NA') production_desc_code,
	 (
	    select   distinct nvl(production_desc_code, 'NA')
	    from     inbound_coil
	    where    coil_number = coil.coil_org_num
	 ) production_desc_code,

	 --nvl(coil.vo, 'NA') vo,
	 (
	    select   distinct nvl(vo, 'NA')
	    from     inbound_coil
	    where    coil_number = coil.coil_org_num
	 ) vo,

	 --nvl(coil.customer_po, 'NA') customer_po,
	 (
	    select   distinct nvl(customer_po, 'NA')
	    from     inbound_coil
	    where    coil_number = coil.coil_org_num
	 ) customer_po,

	 customer_order.orig_customer_po,
	 order_item.enduser_part_num,
	 abis_x12_skid.table67_material_class,
	 abis_x12_skid.table70_material_status_op,
	 coil.net_wt_balance,
	 coil.net_wt,
	 coil.lfeed
  from	 sheet_skid
	 join sheet_skid_detail on sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num
	 join production_sheet_item on sheet_skid_detail.prod_item_num = production_sheet_item.prod_item_num
	 join coil on coil.coil_abc_num = production_sheet_item.coil_abc_num
	 join ab_job on ab_job.ab_job_num = sheet_skid.ab_job_num
	 join customer_order on customer_order.order_abc_num = ab_job.order_abc_num
	 join order_item on order_item.order_abc_num = ab_job.order_abc_num and order_item.order_item_num = ab_job.order_item_num
	 left outer join abis_x12_skid on abis_x12_skid.abis_skid_status = sheet_skid.skid_sheet_status
  where  coil.customer_id = 3061
  and	 sheet_skid.skid_sheet_status in (16, 1, 4, 7, 13, 5, 2, 12, 8, 15, 10) --Skid status: 2:Ready; 4:OnHold; 8:Wh-ready; 10:wh-sort;  13:Partial Ready; 16:Hold For Cert
  order by sheet_skid.sheet_skid_num;

  skid_rec skid_cursor%ROWTYPE;

  /*
  cursor scrap_cursor is
  select scrap_skid.scrap_skid_display_num,
	 scrap_skid.scrap_handling_type,
	 scrap_skid.scrap_type,
	 scrap_type_desc.scrap_type_desc,
	 scrap_skid.scrap_cust_po,
	 scrap_skid.scrap_location scrap_location,
	 scrap_skid.scrap_net_wt,
	 scrap_skid.scrap_tare_wt,
	 scrap_status_desc.scrap_status_desc scrap_status_desc,
	 scrap_skid.scrap_ab_job_num,
	 scrap_skid.scrap_alloy2,
	 scrap_skid.scrap_temper,
	 scrap_skid.scrap_date,
	 return_scrap_item.return_item_net_wt,
	 return_scrap_item.ab_job_num,
	 return_scrap_item.return_item_date,
	 return_scrap_item.return_scrap_item_num,
	 customer.customer_short_name,
	 scrap_skid.skid_scrap_status,
	 return_scrap_item.coil_abc_num,
	 nvl(coil.coil_org_num, '') coil_org_num,
	 nvl(to_char(coil.coil_gauge), '') coil_gauge,
	 nvl(to_char(coil.coil_width), '') coil_width,
	 nvl(to_char(coil.net_wt_balance), '') net_wt_balance,
	 nvl(to_char(coil.lot_num), '') lot_num,
	 nvl(coil.production_desc_code, 'NA') production_desc_code,
	 nvl(coil.vo, 'NA') vo,
	 nvl(coil.customer_po, 'NA') customer_po,
	 scrap_skid.scrap_skid_num,
	 customer_order.orig_customer_po,
	 order_item.enduser_part_num,
	 nvl(abis_scrap_status_x12.table70_material_status_op, 'NA') table70_material_status_op,
	 nvl(abis_scrap_type_x12.table67_material_class, 'NA') table67_material_class
  from	 return_scrap_item
	 join ab_job on ab_job.ab_job_num = return_scrap_item.ab_job_num
	 join customer_order on customer_order.order_abc_num = ab_job.order_abc_num
	 join order_item on order_item.order_abc_num = ab_job.order_abc_num and order_item.order_item_num = ab_job.order_item_num
	 join scrap_skid_detail on return_scrap_item.return_scrap_item_num = scrap_skid_detail.return_scrap_item_num
	 join scrap_skid on scrap_skid.scrap_skid_num = scrap_skid_detail.scrap_skid_num
	 join scrap_type_desc on scrap_type_desc.scrap_type = scrap_skid.scrap_type
	 join scrap_status_desc on scrap_status_desc.scrap_status = scrap_skid.skid_scrap_status
	 join customer on customer.customer_id = scrap_skid.customer_id
	 left outer join coil on coil.coil_abc_num = return_scrap_item.coil_abc_num
	 left outer join abis_scrap_status_x12 on abis_scrap_status_x12.abis_scrap_status = scrap_skid.skid_scrap_status
	 left outer join abis_scrap_type_x12 on abis_scrap_type_x12.abis_scrap_type = scrap_skid.scrap_type
  where  customer.customer_id = al_customer_id
  and	 (
	       scrap_skid.skid_scrap_status in (1, 2, 4) --1-InProcess; 2-Ready; 4-OnHold
	   and scrap_skid.scrap_type in (1, 3, 5, 6, 7, 8, 10, 11) --1-Rej.Sheet-Mill, 3-Others, 5-Rej.Sheet-Process, 6-Sample, 7-Tote, 8-Edge Trim, 10-Full Sheet, 11-Cut Out
	 )
  order by scrap_skid.scrap_skid_num;

  scrap_rec scrap_cursor%ROWTYPE;
  */

  cursor coil_cursor is
  select coil.coil_abc_num,
	 coil.coil_org_num,
	 NVL(coil.coil_mid_num, 'NA') coil_mid_num,
	 NVL(coil.part_num, 'NA') part_num,
	 NVL(coil.supplier_sales_num, 'NA') supplier_sales_num,
	 NVL(coil.purchase_order_num, 'NA') purchase_order_num,
	 coil.net_wt,
	 coil.lfeed,
	 coil.net_wt_balance,
	 coil.coil_status,
	 coil.coil_gauge,
	 coil.coil_width,
	 coil.lot_num,
	 abis_x12_coil.table67_material_class,
	 abis_x12_coil.table70_material_status_op,

	 --nvl(coil.production_desc_code, 'NA') production_desc_code,
	 (
	    select   distinct nvl(production_desc_code, 'NA')
	    from     inbound_coil
	    where    coil_number = coil.coil_org_num
	 ) production_desc_code,

	 --nvl(coil.vo, 'NA') vo,
	 (
	    select   distinct nvl(vo, 'NA')
	    from     inbound_coil
	    where    coil_number = coil.coil_org_num
	 ) vo,

	 --nvl(coil.customer_po, 'NA') customer_po,
	 (
	    select   distinct nvl(customer_po, 'NA')
	    from     inbound_coil
	    where    coil_number = coil.coil_org_num
	 ) customer_po
  from	 coil
	 left outer join abis_x12_coil on abis_x12_coil.abis_coil_status = coil.coil_status
  where  customer_id = 3061
  and  coil_status in (1, 2, 4, 11, 12, 7, 3, 8, 6, 14) -- coil status: 0:Done; 1:InProcess; 2:New; 3:Rejected; 4:OhHold; 6:Return; 7:Rebanded; 8: Retry; 10:Gone;  11:QAOnHold; 12:Ready for Ownership Transfer;  14:Scrap
  --and  coil_status in (1, 4, 11, 12, 7, 3, 8, 6, 14)
  order by coil_abc_num;

  coil_rec coil_cursor%ROWTYPE;



  /*cursor coil_cursor_donebutnotshipped is
  select distinct c.coil_abc_num, c.coil_org_num, NVL(c.coil_mid_num, 'NA') coil_mid_num, NVL(c.part_num, 'NA') part_num, NVL(c.supplier_sales_num, 'NA') supplier_sales_num, NVL(c.purchase_order_num, 'NA') purchase_order_num,  c.net_wt_balance, c.coil_status
  from coil c, sheet_skid ss, production_sheet_item psi, sheet_skid_detail ssd
  where c.coil_abc_num = psi.coil_abc_num and
	psi.prod_item_num = ssd.prod_item_num and
	ssd.sheet_skid_num = ss.sheet_skid_num and
	c.coil_status = 0 and	 -- coil status: 0:Done; 1:InProcess; 2:New; 3:Rejected; 4:OhHold; 6:Returned; 7:Rebanded  11:QAOnHold
	ss.skid_sheet_status <> 0 and
	c.customer_id = al_customer_id	 ;  */

  type edi_846_tabletype is table of varchar2(32767) index by binary_integer;
  edi_846 edi_846_tabletype;

  j binary_integer := 0;
  i integer := 0;



  --edi_file_prefix VARCHAR2(50) := 'S_novelis_kingston_';


--  li_coil_abc_num coil.coil_abc_num%type;
--  ls_coil_org_num coil.coil_org_num%type;
--  ls_coil_mid_num coil.coil_mid_num%type;
--  ls_part_num coil.part_num%type;
--  ls_supplier_sales_num coil.supplier_sales_num%type;
--  ls_purchase_order_num coil.purchase_order_num%type;
--  li_total_skid_net_wt sheet_skid.sheet_net_wt%type;
--  li_total_skid_tare_wt sheet_skid.sheet_tare_wt%type;
--  li_total_net_wt coil.net_wt%type;
--  li_total_gross_wt coil.net_wt%type;

  edi_location varchar2(50) := '/templar/templar/incoming/senddata';
  edi_file varchar2 (50) := 'edi_out.txt';
  edi_file_handle UTL_FILE.FILE_TYPE;

   -- `005159199' Cleveland-Cliffs Steel LLC (Indiana Harbor)
   -- `613460476' Cleveland-Cliffs Kote Inc.
   -- `003913423' Cleveland-Cliffs Burns Harbor LLC
   -- `122373918' Cleveland-Cliffs Cleveland Works LLC

begin
   li_item_count := 0;

   select customer_duns_number
   into   ls_duns
   from   customer
   where  customer_id = 3061;

   SELECT EDI_ST_LOG_SEQ.NEXTVAL INTO li_st_log FROM dual;
   SELECT EDI_GS_LOG_SEQ.NEXTVAL INTO li_gs_log FROM dual;
   ls_today_short := to_char(SYSDATE, 'yymmdd');
   ls_today := to_char(SYSDATE, 'yyyymmdd');
   ls_now := to_char(SYSDATE, 'hh24mi');

   --Production
   edi_846(j) := 'ISA*00*	   *00* 	 *01*039630926T     *01*606072130      *'
    ||	ls_today_short	|| '*' ||  ls_now  || '*U*00401*' ||
		     lpad(to_char(li_gs_log),9,'0') || '*0*P*|' || '~'; j := j + 1;

   --Development
   --edi_846(j) := 'ISA*00*	     *00*	   *ZZ*2NDSFTP	      *01*606072130	 *'
   -- ||  ls_today_short  || '*' ||  ls_now  || '*U*00401*' ||
   --		       lpad(to_char(li_gs_log),9,'0') || '*0*T*:' || '~'; j := j + 1;

   --Production
   edi_846(j) := 'GS*IB*039630926T*606072130*' ||  ls_today  || '*' || ls_now || '*' || li_gs_log || '*X*004010' || '~'; j := j + 1;

   --Development
   --edi_846(j) := 'GS*IB*2NDSFTP*606072130*' ||  ls_today  || '*' || ls_now || '*' || li_gs_log || '*X*004010'; j := j + 1;

   edi_846(j) := 'ST*846*' || li_st_log || '~'; j := j + 1;
   edi_846(j) := 'BIA*00*AA*' || li_st_log || '*' || ls_today  || '*' || ls_now || '~'; j := j + 1;
   edi_846(j) := 'DTM*184*' || ls_today || '*' || ls_now || '*ET' || '~'; j := j + 1;

   ls_duns_cliff := '606072130';
   edi_846(j) := 'N1*SU**1'  ||  ls_duns || '*' || ls_duns_cliff || '~'; j := j + 1; -- 'MF': Steel Producer; ls_duns: Customer DUNS

   ls_duns_albl := '039630926';
   edi_846(j) := 'N1*OU**1*' || ls_duns_albl || '~'; j := j + 1;     --'OU': Outside Processor;  '03-963-0926': ABCo's DUNS#

   --return 1; --TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

/*
   open skid_cursor;
   fetch skid_cursor
   into skid_rec;

   open scrap_cursor;
   fetch scrap_cursor
   into scrap_rec;

   open coil_cursor;
   fetch coil_cursor
   into coil_rec;
*/

   --*********** Sheet skids *************************************


   --This loop is for TEST ONLY
   --for skid_rec in skid_cursor loop
   --  li_skid_count := li_skid_count + 1;
   --end loop;

   --edi_846(j) := '*** Skid Count: ' || to_char(li_skid_count) || '   TEST ONLY TEST ONLY TEST ONLY'; j := j + 1;

   --edi_846(j) := '---------------------- Beginning of skid ----------------------------------'; j := j + 1;

   for skid_rec in skid_cursor loop
     li_skid_count := li_skid_count + 1;
     li_item_count := li_item_count + 1;
     --ls_item_count_string := to_char(li_item_count, '0009');
     ls_item_count_string := to_char(li_item_count);

     ls_table67_mat_class_skid := skid_rec.table67_material_class;
     ls_table70_mat_status_skid := skid_rec.table70_material_status_op;
     ls_lot_num := skid_rec.lot_num;

      --edi_846(j) := 'LIN**PO*' || skid_rec.purchase_order_num || '*BP*' || skid_rec.part_num || '*IN*' || skid_rec.prod_item_num || '*VN*' || skid_rec.coil_org_num || '*PK*' || skid_rec.sheet_skid_num || '*VO*' || skid_rec.supplier_sales_num; j := j + 1;
      --edi_846(j) := 'LIN*0001*VO*' || skid_rec.orig_customer_po || '*PO*' || '01' || '*SN*' || skid_rec.coil_org_num || '*HN*' || ls_lot_num; j := j + 1;
      edi_846(j) := 'LIN*' || trim(ls_item_count_string) || '*VO*' || skid_rec.vo || '*PO*' || skid_rec.customer_po || '*SN*' || skid_rec.coil_org_num || '~'; j := j + 1;

      --li_skid_status_albl := skid_rec.skid_sheet_status;

      --edi_846(j) := '*** Skid Status: ' || to_char(li_skid_status_albl); j := j + 1;

      --if li_skid_status_albl <> 4 then --OnHold
      --Table AISI STEEL CODE TABLE #70 (Material Status-OP Codes). For PID*S*MA

      --Email from Lisa received on Mon 5/18/2026 2:14 PM
      --Remove PID06 from PID*S*MA and *MAC segments
      --edi_846(j) := 'PID*S*MAC*ST*' || ls_table67_mat_class_skid || '***67'; j := j + 1; --01: Prime (sheet skid) from AISI material classification code table 67. '67': Table 67
      edi_846(j) := 'PID*S*MAC*ST*' || ls_table67_mat_class_skid || '~'; j := j + 1; --01: Prime (sheet skid) from AISI material classification code table 67. '67': Table 67

      --edi_846(j) := 'PID*S*MA*ST*' || ls_table70_mat_status_skid || '***70' ; j := j + 1; --Table AISI STEEL CODE TABLE #70 (Material Status-OP Codes)
      edi_846(j) := 'PID*S*MA*ST*' || ls_table70_mat_status_skid || '~' ; j := j + 1; --Table AISI STEEL CODE TABLE #70 (Material Status-OP Codes)
      --else --li_skid_status_albl = 4 - 'OnHold'
       -- ls_skid_status_cliff := '2';	 --'Hold for QA Release' from table 68
       -- edi_846(j) := 'PID*S*QAS*ST*' || ls_skid_status_cliff || '***68'; j := j + 1;
      --end if;

      edi_846(j) := 'MEA*WT*WT*' || skid_rec.prod_item_net_wt || '*01' || '~'; j := j + 1;     --'01' means LBS
      --edi_846(j) := 'MEA*WT*WT*' || skid_rec.prod_item_net_wt || '*24'; j := j + 1;	  --'24' means Theo LBS
      --edi_846(j) := 'MEA*PD*G*' || skid_rec.skid_gross_wt || '*LB'; j := j + 1;
      --edi_846(j) := 'MEA*PD*TH*' || to_char(skid_rec.coil_gauge) || '*ED'; j := j + 1;  --'ED' means Inches, Decimal-Nominal
      --edi_846(j) := 'MEA*PD*WD*' || to_char(skid_rec.coil_width) || '*IN'; j := j + 1;  --'IN' means Inches

      --ll_coil_length := (skid_rec.net_wt_balance / skid_rec.net_wt) * skid_rec.lfeed;

      select   f_get_long_length_4order_item(order_item.order_abc_num, order_item.order_item_num)
      into     ll_long_part_length
      from     sheet_skid
	       join ab_job on ab_job.ab_job_num = sheet_skid.ab_job_num
	       join order_item on order_item.order_abc_num = ab_job.order_abc_num and order_item.order_item_num = ab_job.order_item_num
      where    sheet_skid.sheet_skid_num = skid_rec.sheet_skid_num;

      --************ Incorrect. Should be part length... I guess
      --edi_846(j) := 'MEA*PD*LN*' || to_char(ll_coil_length) || '*LF'; j := j + 1;	     --'LF' means LInear Feet
      --edi_846(j) := 'MEA*PD*LN*' || to_char(ll_long_part_length) || '*IN'; j := j + 1;    --'IN' means Inches

      --edi_846(j) := 'MEA*CT*NL*' || to_char(skid_rec.prod_item_pieces) || '*PC'; j := j + 1; --'PC' means pieces
      --edi_846(j) := 'DTM*009*' || to_char(skid_rec.skid_date, 'YYYYMMDD*HHMI') || '*ET'; j := j + 1;
      edi_846(j) := 'DTM*206*' || ls_today || '*' || ls_now || '*ET' || '~'; j := j + 1;
      --edi_846(j) := 'DTM*105*' || ls_today; j := j + 1;
      edi_846(j) := 'REF*SE*' || to_char(skid_rec.sheet_skid_num) || '~'; j := j + 1;

      --edi_846(j) := 'QTY*01*1'; j := j + 1;

      --edi_846(j) := '--------------------------------------------------------------------'; j := j + 1;

   end loop; --for skid_rec in skid_cursor loop

   --edi_846(j) := '---------------------- End of skid ----------------------------------'; j := j + 1;


   --*********** Scrap skids *************************************

   --This loop is for TEST ONLY


   --This loop is for test only
   --for scrap_rec in scrap_cursor loop
   --  li_scrap_skid_count := li_scrap_skid_count + 1;
   --end loop;

   --edi_846(j) := '*** Scrap Skid Count: ' || to_char(li_scrap_skid_count) || '   TEST ONLY TEST ONLY TEST ONLY'; j := j + 1;


   /*
   for scrap_rec in scrap_cursor loop
      li_scrap_skid_count := li_scrap_skid_count + 1;
      li_item_count := li_item_count + 1;
      --ls_item_count_string := to_char(li_item_count, '0009');
      ls_item_count_string := to_char(li_item_count);

      ll_scrap_skid_num := scrap_rec.scrap_skid_num;
      ls_table67_mat_class_scrap := scrap_rec.table67_material_class;
      ls_table70_mat_status_scrap := scrap_rec.table70_material_status_op;
      ls_lot_num := scrap_rec.lot_num;

      --edi_846(j) := 'LIN**PO*' || scrap_rec.purchase_order_num || '*BP*' || scrap_rec.part_num || '*IN*' || scrap_rec.prod_item_num || '*VN*' || scrap_rec.coil_org_num || '*PK*' || scrap_rec.sheet_skid_num || '*VO*' || scrap_rec.supplier_sales_num; j := j + 1;
      --edi_846(j) := 'LIN*0001*VO*' || scrap_rec.orig_customer_po || '*VN*' || '01' || '*SN*' || scrap_rec.coil_org_num || '*HN*' || ls_lot_num; j := j + 1;
      --edi_846(j) := 'LIN*' || trim(ls_item_count_string) || '*VO*' || scrap_rec.orig_customer_po || '*VN*' || '01' || '*SN*' || scrap_rec.coil_org_num; j := j + 1;
      edi_846(j) := 'LIN*' || trim(ls_item_count_string) || '*VO*' || scrap_rec.vo || '*PO*' || scrap_rec.customer_po || '*SN*' || scrap_rec.coil_org_num || '~'; j := j + 1;

      --edi_846(j) := '*** Scrap Skid Type: ' || to_char(scrap_rec.scrap_type) || '   Scrap Skid Status: ' || to_char(scrap_rec.skid_scrap_status) || '   TEST ONLY TEST ONLY TEST ONLY'; j := j + 1;

      --li_scrap_type_albl := scrap_rec.scrap_type;
      --ls_scrap_type_cliff := ls_table67_mat_class_scrap;

      --Email from Lisa received on Mon 5/18/2026 2:14 PM
      --Remove PID06 from PID*S*MA and *MAC segments
      --edi_846(j) := 'PID*S*MAC*ST*' || ls_table67_mat_class_scrap || '***67'; j := j + 1; --01: Prime (sheet skid) from AISI material classification code table 67. '67': Table 67
      edi_846(j) := 'PID*S*MAC*ST*' || '13' || '~'; j := j + 1; --01: Prime (sheet skid) from AISI material classification code table 67. '67': Table 67

      --li_scrap_status_albl := scrap_rec.skid_scrap_status;
      --ls_scrap_status_cliff := ls_table70_mat_status_scrap;
      --edi_846(j) := 'PID*S*MA*ST*' || ls_table70_mat_status_scrap || '***70'; j := j + 1; --Table AISI STEEL CODE TABLE #70 (Material Status-OP Codes)
      edi_846(j) := 'PID*S*MA*ST*' || 'S' || '~'; j := j + 1; --Table AISI STEEL CODE TABLE #70 (Material Status-OP Codes)
      --else --li_skid_status_albl = 4 - 'OnHold'
      --  ls_scrap_status_cliff := 'E';   --'Hold for QA Release' from table 68
      --  edi_846(j) := 'PID*S*QAS*ST*' || ls_scrap_status_cliff || '***68'; j := j + 1;
      --end if;


      edi_846(j) := 'MEA*WT*WT*' || scrap_rec.return_item_net_wt || '*01' || '~'; j := j + 1;	  --'01' means LBS
      --edi_846(j) := 'MEA*WT*WT*' || scrap_rec.return_item_net_wt || '*24'; j := j + 1;     --'24' means Theo LBS
      --edi_846(j) := 'MEA*PD*G*' || (to_char(scrap_rec.scrap_net_wt + scrap_tare_wt)) || '*LB'; j := j + 1;
      --edi_846(j) := 'MEA*PD*TH*' || to_char(scrap_rec.coil_gauge) || '*ED'; j := j + 1;  --'ED' means Inches, Decimal-Nominal
      --edi_846(j) := 'MEA*PD*WD*' || to_char(scrap_rec.coil_width) || '*IN'; j := j + 1;  --'IN' means Inches

      ll_coil_length := scrap_rec.net_wt_balance / (scrap_rec.coil_width * scrap_rec.coil_gauge);

      ll_long_part_length := 0; --TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

      select max(f_get_long_length_4order_item(order_item.order_abc_num, order_item.order_item_num))
      into   ll_long_part_length
      from   return_scrap_item
	     join ab_job on ab_job.ab_job_num = return_scrap_item.ab_job_num
	     join customer_order on customer_order.order_abc_num = ab_job.order_abc_num
	     join order_item on order_item.order_abc_num = ab_job.order_abc_num and order_item.order_item_num = ab_job.order_item_num
	     join scrap_skid_detail on return_scrap_item.return_scrap_item_num = scrap_skid_detail.return_scrap_item_num
	     join scrap_skid on scrap_skid.scrap_skid_num = scrap_skid_detail.scrap_skid_num
      where  scrap_skid.scrap_skid_num = scrap_rec.scrap_skid_num
      and    return_scrap_item.return_scrap_item_num = scrap_rec.return_scrap_item_num;

      --************ Incorrect. Should be part length... I guess
      --edi_846(j) := 'MEA*PD*LN*' || to_char(ll_coil_length) || '*LF'; j := j + 1;	     --'LF' means LInear Feet
      --edi_846(j) := 'MEA*PD*LN*' || to_char(ll_long_part_length) || '*IN'; j := j + 1;    --'IN' means Inches

      --There are no pieces for a scrap skid
      --edi_846(j) := 'MEA*CT*NL*' || to_char(scrap_rec.skid_pieces) || '*PC'; j := j + 1; --'PC' means pieces	???????????
      --edi_846(j) := 'DTM*009*' || to_char(scrap_rec.return_item_date, 'YYYYMMDD*HHMI') || '*ET'; j := j + 1;
      edi_846(j) := 'DTM*206*' || ls_today || '*' || ls_now || '*ET' || '~'; j := j + 1;
      --edi_846(j) := 'DTM*105*' || ls_today; j := j + 1;
      edi_846(j) := 'REF*SE*' || to_char(scrap_rec.scrap_skid_num) || '~'; j := j + 1;

      --edi_846(j) := 'QTY*01*1'; j := j + 1;


   end loop; --for scrap_rec in skid_cursor loop
   */


   --This loop is for TEST ONLY
   --for coil_rec in coil_cursor loop
   --  li_coil_count := li_coil_count + 1;
   --end loop;

   --edi_846(j) := '*** Coil Count: ' || to_char(li_coil_count) || '   TEST ONLY TEST ONLY TEST ONLY'; j := j + 1;

   --edi_846(j) := '---------------------- Beginning of coil ----------------------------------'; j := j + 1;

   for coil_rec in coil_cursor loop
     li_coil_count := li_coil_count + 1;
     li_item_count := li_item_count + 1;
     --ls_item_count_string := to_char(li_item_count, '0009');
     ls_item_count_string := to_char(li_item_count);

     ls_coil_org_num := coil_rec.coil_org_num;
     ll_coil_abc_num := coil_rec.coil_abc_num;
     ls_production_desc_code := coil_rec.production_desc_code;
     --ls_production_desc_code := '?'; --TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY
     --select to_char(li_coil_count, '0009') into ls_coil_count_string from dual;
     ls_vendor_item_num := '01';
     ls_table67_mat_class_coil := coil_rec.table67_material_class;
     ls_table70_mat_status_coil := coil_rec.table70_material_status_op;

     --edi_846(j) := 'LIN*' || ls_coil_count_string || '*VO*' || coil_rec.purchase_order_num || '*VN*' || ls_vendor_item_num || '*SN*' || coil_rec.coil_org_num || '*HN*' || coil_rec.lot_num; j := j + 1;
     edi_846(j) := 'LIN*' || trim(ls_item_count_string) || '*VO*' || coil_rec.vo || '*PO*' || coil_rec.customer_po || '*SN*' || coil_rec.coil_org_num || '~'; j := j + 1;

      --Coils data
      li_coil_status := coil_rec.coil_status;

      --edi_846(j) := 'Coil Status: ' || to_char(li_coil_status) || '  <== TEST ONLY TEST ONLY TEST ONLY '; j := j + 1;

      if li_coil_status <> 2 then --New
	ls_coil_classification := ls_table67_mat_class_coil;	 --Table 67
	ls_coil_status_cliff := ls_table70_mat_status_coil;	 --Table 70
      else
	ls_coil_classification := ls_production_desc_code;  --Table 67


	select	 count(*)
	into	 li_count
	from	 process_coil
	where	 coil_abc_num = ll_coil_abc_num;


	--ls_coil_classification := ls_production_desc_code;  --Table 67

	--li_count := 0; --TEST ONLY TEST ONLY TEST ONLY TEST ONLY TEST ONLY

	if li_count > 0 then --coil is assigned to a job
	  ls_coil_status_cliff := 'C';	      --Table 70. Coil is assigned to a job
	  --ls_coil_classification_qas := 'N/A';--Table 68
	else
	  ls_coil_status_cliff := 'Q';	      --Table 70. Coil is not assigned to a job
	  --ls_coil_classification_qas := 'N/A';--Table 68
	end if;
      end if;

      --edi_846(j) := '*** ALBL coil status: ' || to_char(li_coil_status) || '	 TEST ONLY TEST ONLY TEST ONLY'; j := j + 1;

      --Email from Lisa received on Mon 5/18/2026 2:14 PM
      --Remove PID06 from PID*S*MA and *MAC segments
      --if ls_coil_classification <> 'N/A' then
	--edi_846(j) := 'PID*S*MAC*ST*' || trim(ls_production_desc_code) || '***67'; j := j + 1; --Table 67
	edi_846(j) := 'PID*S*MAC*ST*' || trim(ls_production_desc_code) || '~'; j := j + 1; --Table 67
      --end if;

      --if ls_coil_status_cliff <> 'N/A' then
	--edi_846(j) := 'PID*S*MA*ST*' || trim(ls_coil_status_cliff) || '***70'; j := j + 1;	--Table 70
	edi_846(j) := 'PID*S*MA*ST*' || trim(ls_coil_status_cliff) || '~'; j := j + 1;	  --Table 70
      --end if;

      --if ls_coil_classification_qas <> 'N/A' then
      --  edi_846(j) := 'PID*S*QAS*ST*' || ls_coil_classification_qas || '***68'; j := j + 1;  --Table 68
      --end if;

      edi_846(j) := 'MEA*WT*WT*' || to_char(coil_rec.net_wt_balance) || '*01' || '~'; j := j + 1;  --'01' means LBS
      --edi_846(j) := 'MEA*WT*WT*' || to_char(coil_rec.net_wt_balance) || '*24'; j := j + 1;  --'24' means Theoretical Pounds
      --edi_846(j) := 'MEA*PD*TH*' || to_char(coil_rec.coil_gauge) || '*ED'; j := j + 1;  --'ED' means Inches, Decimal-Nominal
      --edi_846(j) := 'MEA*PD*WD*' || to_char(coil_rec.coil_width) || '*E8'; j := j + 1;  --'ED' means Inches, Decimal-Actual
      --edi_846(j) := 'QTY*01*1'; j := j + 1;

      --ll_coil_length := coil_rec.net_wt_balance / (coil_rec.coil_width * coil_rec.coil_gauge);
      --ll_coil_length := (coil_rec.net_wt_balance / coil_rec.net_wt) * coil_rec.lfeed;
      --edi_846(j) := 'MEA*PD*LN*' || to_char(ll_coil_length) || '*LF'; j := j + 1;  --'LF' means Lineal Feet

      --We don't have info for the 2 lines below
      ls_coil_od := 'Not On DB';
      ls_coil_id := 'Not On DB';
      --edi_846(j) := 'MEA*PD*OD*' || to_char(ll_coil_od) || '*ED'; j := j + 1;  --'ED' means Inches, Decimal-Nominal
      --edi_846(j) := 'MEA*PD*OD*' || ls_coil_od || '*ED'; j := j + 1;	--'ED' means Inches, Decimal-Nominal
      --edi_846(j) := 'MEA*PD*ID*' || to_char(ll_coil_id) || '*ED'; j := j + 1;  --'ED' means Inches, Decimal-Nominal
      --edi_846(j) := 'MEA*PD*OD*' || ls_coil_id || '*ED'; j := j + 1;	--'ED' means Inches, Decimal-Nominal

      if coil_rec.coil_status = 2 then
	select to_char(coil_entry_date, 'YYYYMMD*HHMI'), to_char(coil_entry_date, 'YYYYMMD*HHMI')
	into   ls_coil_process_date, ls_qa_status_date
	from   coil
	where  coil_abc_num = coil_rec.coil_abc_num;
      else
	select to_char(max(coil_track_date), 'YYYYMMD*HHMI'), to_char(max(coil_track_date), 'YYYYMMD')
	into   ls_coil_process_date, ls_qa_status_date
	from   coil_track
	where  coil_abc_num = coil_rec.coil_abc_num;
      end if;

      --edi_846(j) := 'DTM*009*' || ls_coil_process_date || '*ET'; j := j + 1;	--'009' means Process. ET' means Eastern Time
      edi_846(j) := 'DTM*206*' || ls_today || '*' || ls_now || '*ET' || '~'; j := j + 1;  --'206' means OP Status. ET' means Eastern Time
      --edi_846(j) := 'DTM*105*' || to_char(sysdate, 'YYYYMMDD'); j := j + 1;		--'105' means QA Status
      edi_846(j) := 'REF*SE*' || to_char(coil_rec.coil_abc_num) || '~'; j := j + 1;

      --edi_846(j) := '--------------------------------------------------------------------'; j := j + 1;

   end loop; --for coil_rec in coil_cursor loop

   --edi_846(j) := '---------------------- End of coil ----------------------------------'; j := j + 1;

   edi_846(j) := 'CTT*' || to_char(li_coil_count + li_skid_count + li_scrap_skid_count) || '~'; j := j + 1;
   edi_846(j) := 'SE*' || to_char(j-1) || '*' || li_st_log || '~'; j := j + 1;
   edi_846(j) := 'GE*1*' || li_gs_log || '~'; j := j + 1;
   edi_846(j) := 'IEA*1*' || LPAD(to_char(li_gs_log),9,'0') || '~';

   --if li_coil_count > 0 or li_skid_count > 0 or li_scrap_skid_count > 0 then
   if li_item_count > 0 then
     SELECT edi_file_id_seq.NEXTVAL INTO li_edi_846_id FROM DUAL;
-- Changed file suffix on 07/27/2016 to accomodate GXS change to SFTP - Patrick Reynolds
--     edi_file := 'S_novelis_kingston_' || to_char(li_edi_846_id) || '.846';
     edi_file := edi_file_prefix || to_char(li_edi_846_id) || '.edi';
     edi_file_handle := utl_file.fopen(edi_location,edi_file,'W');
     --write edi to file
     FOR i IN 0..j LOOP
	 utl_file.put_line(edi_file_handle,edi_846(i));
	 END LOOP;
     utl_file.fclose(edi_file_handle);

     -- for verifying 997
      /*INSERT INTO outbound_edi_transaction(edi_file_id,duns_from,duns_to,interchange_control_number,
      group_control_number,transaction_time,edi_file_name,fa_receive_status,customer_id,set_control_num,transaction_type_id)
      VALUES(li_edi_846_id,'039630926',ls_duns,li_gs_log,li_gs_log, sysdate, edi_file,0,al_customer_id,li_st_log,'846');
      COMMIT;*/

      /*edi_file := 'send_edi_log';
      edi_file_handle := utl_file.fopen(edi_location,edi_file,'a');
      utl_file.put_line(edi_file_handle,'Type: 846');
      utl_file.put_line(edi_file_handle,'Sending Time: ' || to_char(sysdate, 'MM/DD/YY HH24:MI'));
      utl_file.put_line(edi_file_handle,'Interchange Control #: ' || li_gs_log);
      utl_file.put_line(edi_file_handle,'Group Control #: ' || li_gs_log);
      utl_file.put_line(edi_file_handle,'Set Control #: ' || li_st_log);
      utl_file.put_line(edi_file_handle,'');
      utl_file.put_line(edi_file_handle,'');
      utl_file.fclose(edi_file_handle);*/

      commit;
    else
      SELECT edi_file_id_seq.NEXTVAL INTO li_edi_846_id FROM DUAL;
-- Changed file suffix on 07/27/2016 to accomodate GXS change to SFTP - Patrick Reynolds
--     edi_file := 'S_novelis_kingston_' || to_char(li_edi_846_id) || '.846';
     edi_file := edi_file_prefix || to_char(li_edi_846_id) || '.edi';
     edi_file_handle := utl_file.fopen(edi_location,edi_file,'W');
     --write edi to file
     utl_file.put_line(edi_file_handle,'Nothing to report.');
     utl_file.fclose(edi_file_handle);
    end if;


  --return 1;

  EXCEPTION
     WHEN dup_val_on_index then
     rollback;
     return;
     WHEN UTL_FILE.INVALID_PATH THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN;
     WHEN UTL_FILE.INVALID_MODE THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN;
    WHEN UTL_FILE.INVALID_OPERATION THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN;
    WHEN UTL_FILE.WRITE_ERROR THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN;
    WHEN OTHERS THEN
     UTL_FILE.FCLOSE_ALL;
      RETURN;


  --return 1;
end p_846_cleveland_cliff_ccsc;
/
