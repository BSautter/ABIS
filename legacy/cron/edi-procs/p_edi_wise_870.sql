  CREATE OR REPLACE PROCEDURE "DBO"."P_EDI_WISE_870" is
/*Send 870 to Wise Alloys LLC
Customer_id = 1364
*/
/*
If sucess, then return 1
If fail, return the coil_abc_num that fails
*/
li_rc integer := 0;
li_count integer:=0;
ll_customer_id integer :=1364;

--As per customer's request, one 870 file contains skids under one coil
Cursor cur_coil_abc_num IS
SELECT distinct coil.coil_abc_num
FROM 		SHEET_SKID, production_sheet_item, sheet_skid_detail,coil
WHERE	     (sheet_skid.skid_sheet_status in ( 2, 4)) and
	     (production_sheet_item.prod_item_edi870_date is NULL ) and
	     (sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num) and
			       (sheet_skid_detail.prod_item_num = production_sheet_item.prod_item_num) and
			       (coil.coil_abc_num = production_sheet_item.coil_abc_num ) and
	     (coil.customer_id = ll_customer_id);

begin

SELECT count(*) into li_count
FROM 		SHEET_SKID, production_sheet_item, sheet_skid_detail,coil
WHERE	     (sheet_skid.skid_sheet_status in ( 2, 4)) and
	     (production_sheet_item.prod_item_edi870_date is NULL ) and
	     (sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num) and
			       (sheet_skid_detail.prod_item_num = production_sheet_item.prod_item_num) and
			       (coil.coil_abc_num = production_sheet_item.coil_abc_num ) and
	     (coil.customer_id = ll_customer_id);

if li_count <> 0 then
   for coil_abc_num_rec in cur_coil_abc_num loop
       --As per customer's request, one 870 file contains skids under one coil
       li_rc := f_edi_wise_870_by_coil(coil_abc_num_rec.coil_abc_num);
       /*
       if li_rc <> 1 then
       return ;
       end if;
       */
   end loop;

end if;

end P_EDI_WISE_870;
/
