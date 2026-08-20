function f_846_cleveland_cliff
(
   al_customer_id in customer.customer_id%TYPE
)

return number is

  --al_customer_id int := 1153;          --Novelis Kingston : 1153
                                       --Novelis Oswego : 1459
  ls_duns                 customer.customer_duns_number%TYPE; --'241003755':Novelis Kingston's DUNS#
                                       --'003980216':Novelis Oswego's DUNS#
  li_skid_status_albl     sheet_skid.skid_sheet_status%TYPE;
  ls_skid_status_cliff    varchar2(3);
  li_coil_status          coil.coil_status%TYPE;
  ls_coil_status          varchar2(2);
  coil_length             number(8);
  ls_duns_cliff           varchar2(32);
  ls_duns_albl            varchar2(32);
  ll_long_part_length     number(10,5);

  cursor skid_cursor is
  select production_sheet_item.prod_item_num,
         sheet_skid.sheet_skid_num,
         production_sheet_item.prod_item_net_wt,
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
         NVL(coil.purchase_order_num, 'NA') purchase_order_num
  from   sheet_skid
         join sheet_skid_detail on sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num
         join production_sheet_item on sheet_skid_detail.prod_item_num = production_sheet_item.prod_item_num
         join coil on coil.coil_abc_num = production_sheet_item.coil_abc_num
  where  sheet_skid.skid_sheet_status in (2, 4, 13) and  --Skid status: 2:Ready; 4:OnHold; 13:Partial Ready
         coil.customer_id = al_customer_id;

  skid_rec skid_cursor%ROWTYPE;


  cursor coil_cursor is
  select coil_abc_num,
         coil_org_num,
         NVL(coil_mid_num, 'NA') coil_mid_num,
         NVL(part_num, 'NA') part_num,
         NVL(supplier_sales_num, 'NA') supplier_sales_num,
         NVL(purchase_order_num, 'NA') purchase_order_num,
         net_wt_balance,
         coil_status
  from   coil
  where  coil_status in (1, 2, 3, 4, 6, 7, 11) -- coil status: 0:Done; 1:InProcess; 2:New; 3:Rejected; 4:OhHold; 6:Return; 7:Rebanded; 8: Retry; 10:Gone;  11:QAOnHold
  and    customer_id = al_customer_id;

  coil_rec coil_cursor%ROWTYPE;



/*
  cursor scrap_cursor is
  select scrap_skid.scrap_skid_display_num,
         scrap_skid.scrap_handling_type,
         (select scrap_type_desc from scrap_type_desc where scrap_type = scrap_skid.scrap_type ) scrap_type_desc,
         scrap_skid.scrap_cust_po,
         scrap_skid.scrap_location scrap_location,
         scrap_skid.scrap_net_wt,
         scrap_skid.scrap_tare_wt,
         (select scrap_status_desc from scrap_status_desc where scrap_status = scrap_skid.skid_scrap_status) scrap_status_desc,
         scrap_skid.scrap_ab_job_num,
         scrap_skid.scrap_alloy2,
         scrap_skid.scrap_temper,
         scrap_skid.scrap_date,
         --0 duration,
         return_scrap_item.return_item_net_wt,
         return_scrap_item.ab_job_num,
         return_scrap_item.return_item_date,
         customer.customer_short_name,
         scrap_skid.scrap_type,
         scrap_skid.skid_scrap_status,
         return_scrap_item.coil_abc_num,
         scrap_skid.scrap_skid_num
  from   return_scrap_item
         join scrap_skid_detail on return_scrap_item.return_scrap_item_num = scrap_skid_detail.return_scrap_item_num
         join scrap_skid on scrap_skid.scrap_skid_num = scrap_skid_detail.scrap_skid_num
         join customer on customer.customer_id = scrap_skid.customer_id
         left outer join coil on coil.coil_abc_num = return_scrap_item.coil_abc_num
  where  customer.customer_id = al_customer_id
  and    scrap_skid.skid_scrap_status in (1, 2, 3, 4); --1: InProcess; 2: Ready; 3: Canceled; 4: OnHold

  scrap_rec scrap_cursor%ROWTYPE;
*/


  /*cursor coil_cursor_donebutnotshipped is
  select distinct c.coil_abc_num, c.coil_org_num, NVL(c.coil_mid_num, 'NA') coil_mid_num, NVL(c.part_num, 'NA') part_num, NVL(c.supplier_sales_num, 'NA') supplier_sales_num, NVL(c.purchase_order_num, 'NA') purchase_order_num,  c.net_wt_balance, c.coil_status
  from coil c, sheet_skid ss, production_sheet_item psi, sheet_skid_detail ssd
  where c.coil_abc_num = psi.coil_abc_num and
        psi.prod_item_num = ssd.prod_item_num and
        ssd.sheet_skid_num = ss.sheet_skid_num and
        c.coil_status = 0 and    -- coil status: 0:Done; 1:InProcess; 2:New; 3:Rejected; 4:OhHold; 6:Returned; 7:Rebanded  11:QAOnHold
        ss.skid_sheet_status <> 0 and
        c.customer_id = al_customer_id   ;  */

  type edi_846_tabletype is table of varchar2(32767) index by binary_integer;
  edi_846 edi_846_tabletype;

  j binary_integer := 0;
  i integer := 0;



  --edi_file_prefix VARCHAR2(50) := 'S_novelis_kingston_';
  edi_file_prefix VARCHAR2(50) := 's_cleveland_cliff_846_';
  li_gs_log INTEGER;
  li_st_log INTEGER;
  ls_today_short VARCHAR2(20);
  ls_today VARCHAR2(20);
  ls_now VARCHAR2(20);
  li_coil_count int := 0;
  li_skid_count int := 0;
  li_edi_846_id int;

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



begin

   select customer_duns_number
   into   ls_duns
   from   customer
   where  customer_id = al_customer_id;

   SELECT EDI_ST_LOG_SEQ.NEXTVAL INTO li_st_log FROM dual;
   SELECT EDI_GS_LOG_SEQ.NEXTVAL INTO li_gs_log FROM dual;
   ls_today_short := to_char(SYSDATE, 'yymmdd');
   ls_today := to_char(SYSDATE, 'yyyymmdd');
   ls_now := to_char(SYSDATE, 'hh24mi');

   edi_846(j) := 'ISA*00*          *00*          *01*039630926T     *09*0015049350011G *'
    ||  ls_today_short  || '*' ||  ls_now  || '*U*00401*' || LPAD(to_char(li_gs_log),9,'0') || '*0*P*:'; j := j + 1;
   edi_846(j) := 'GS*IB*039630926T*0015049350011G*' ||  ls_today  || '*' || ls_now || '*' || li_gs_log || '*X*004010'   ; j := j + 1;
   edi_846(j) := 'ST*846*' || li_st_log ; j := j + 1;
   edi_846(j) := 'BIA*00*AA*' || li_st_log || '*' || ls_today; j := j + 1;
   edi_846(j) := 'DTM*184*' || ls_today || '*' || ls_now || '*ET'; j := j + 1;

   -- `005159199' Cleveland-Cliffs Steel LLC (Indiana Harbor)
   -- `613460476' Cleveland-Cliffs Kote Inc.
   -- `003913423' Cleveland-Cliffs Burns Harbor LLC
   -- `122373918' Cleveland-Cliffs Cleveland Works LLC
   ls_duns_cliff := '005159199';
   edi_846(j) := 'N1*MF**'  ||  ls_duns || '*' || ls_duns_cliff; j := j + 1; -- 'MF': Steel Producer; ls_duns: Customer DUNS

   ls_duns_albl := '03-963-0926';
   edi_846(j) := 'N1*OU**1*03-963-0926' || '*' || ls_duns_albl; j := j + 1;     --'OU': Outside Processor;  '03-963-0926': ABCo's DUNS#

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


   for skid_rec in skid_cursor loop


      --edi_846(j) := 'LIN**PO*' || skid_rec.purchase_order_num || '*BP*' || skid_rec.part_num || '*IN*' || skid_rec.prod_item_num || '*VN*' || skid_rec.coil_org_num || '*PK*' || skid_rec.sheet_skid_num || '*VO*' || skid_rec.supplier_sales_num; j := j + 1;
      edi_846(j) := 'LIN*0001*VO*' || skid_rec.purchase_order_num || '*VN*' || '01' || '*SN*' || skid_rec.coil_org_num || '*HN*' || 'Cliffs Heat Number'; j := j + 1;
      /*case skid_rec.skid_sheet_status
        when 2 then ls_skid_status := '1';
        when 4 then ls_skid_status := '3';
        when 13 then ls_skid_status := '8';     /
      end case;      --Skid status: 2:Ready; 4:OnHold; 13:Partial Ready
      edi_846(j) := 'PID*S*MA*ST*'  ||  ls_skid_status; j := j + 1;   -- 8: processing complete  */

      edi_846(j) := 'PID*S*MAC*ST*01***67';j := j + 1; --01: Prime (sheet skid) from AISI material classification code table 67. '67': Table 67

      li_skid_status_albl := skid_rec.skid_sheet_status;

      if li_skid_status_albl <> 4 then --OnHold
        --Table AISI STEEL CODE TABLE #70 (Material Status-OP Codes). For PID*S*MA
        case li_skid_status_albl
          when 0 then -- 'Gone'                                                      ALBL
               ls_skid_status_cliff := 'R';   -- 'Released'
          when 1 then -- 'InProcess'                                                 ALBL
               ls_skid_status_cliff := '7';   -- 'InProcess'
          when 2 then -- 'Ready'                                                     ALBL
               ls_skid_status_cliff := 'A';   -- 'Scheduled To Ship'
          when 3 then -- 'Canceled'                                                  ALBL
               ls_skid_status_cliff := 'D';   -- 'Obsolete'
          --when 4 then -- 'OnHold'                                                    ALBL
          --     ls_skid_status_cliff := 'I';   -- 'Hold for Inspection (Processor)'
          when 5 then -- 'PreReCap'                                                  ALBL
               ls_skid_status_cliff := '7';   -- 'InProcess'
          when 6 then -- 'TareOnly'                                                  ALBL
               ls_skid_status_cliff := 'D';   -- 'Obsolete'
          when 7 then -- 'Partial'                                                   ALBL
               ls_skid_status_cliff := '7';   -- 'InProcess'
          when 8 then -- 'Wh-ready'                                                  ALBL
               ls_skid_status_cliff := 'A';   -- 'Scheduled To Ship'
          when 9 then -- 'Wh-coming'                                                 ALBL
               ls_skid_status_cliff := '7';   -- 'InProcess'
          when 10 then --'Wh-sort'                                                   ALBL
               ls_skid_status_cliff := '7';   -- 'InProcess'
          when 11 then --'Gone'                                                      ALBL
               ls_skid_status_cliff := 'R';    --'Released'
          when 12 then --'Sample'                                                    ALBL
               ls_skid_status_cliff := '?';    --'????????'                          Cleveland-Cliff. COuld not find in table 70.
                                                             --Table 67 has '08' - Experimental/Trial
          when 13 then --'Partial-Rd'                                                ALBL
               ls_skid_status_cliff := '7';   -- 'InProcess'
          when 14 then --'Unfinish'                                                  ALBL
               ls_skid_status_cliff := '7';   -- 'InProcess'
          when 15 then --'WH-Sample'                                                 ALBL
               ls_skid_status_cliff := '?';    --'????????'
                                                             --Table 67 has '08' - Experimental/Trial
          when 16 then --'Hold For Cert'                                             ALBL
               ls_skid_status_cliff := '3';   -- 'Hold for Release'
          else --'99 - Other Itm'                                                    ALBL
               ls_skid_status_cliff := '?';    --'????????'
        end case;

        edi_846(j) := 'PID*S*MA*ST*' || ls_skid_status_cliff || '***70' ;j := j + 1; --Table AISI STEEL CODE TABLE #70 (Material Status-OP Codes)
      else --li_skid_status_albl = 4 - 'OnHold'
        ls_skid_status_cliff := '2';   --'Hold for QA Release' from table 68
        edi_846(j) := 'PID*S*QAS*ST*' || ls_skid_status_cliff || '***68'; j := j + 1;
      end if;


      edi_846(j) := 'MEA*WT*WT*' || skid_rec.prod_item_net_wt || '*01'; j := j + 1;     --'01' means LBS
      edi_846(j) := 'MEA*WT*WT*' || skid_rec.prod_item_net_wt || '*24'; j := j + 1;     --'24' means Theo LBS
      --edi_846(j) := 'MEA*PD*G*' || skid_rec.skid_gross_wt || '*LB'; j := j + 1;
      edi_846(j) := 'MEA*PD*TH*' || to_char(skid_rec.coil_gauge) || '*ED'; j := j + 1;  --'ED' means Inches, Decimal-Nominal
      edi_846(j) := 'MEA*PD*WD*' || to_char(skid_rec.coil_width) || '*IN'; j := j + 1;  --'IN' means Inches

      coil_length := skid_rec.prod_item_net_wt / (skid_rec.coil_width * skid_rec.coil_gauge);

      select   f_get_long_length_4order_item(order_item.order_abc_num, order_item.order_item_num)
      into     ll_long_part_length
      from     sheet_skid
               join ab_job on ab_job.ab_job_num = sheet_skid.ab_job_num
               join order_item on order_item.order_abc_num = ab_job.order_abc_num and order_item.order_item_num = ab_job.order_item_num
      where    sheet_skid.sheet_skid_num = skid_rec.sheet_skid_num;

      --************ Incorrect. Should be part length... I guess
      --edi_846(j) := 'MEA*PD*LN*' || to_char(coil_length) || '*LF'; j := j + 1;          --'LF' means LInear Feet
      edi_846(j) := 'MEA*PD*LN*' || to_char(ll_long_part_length) || '*IN'; j := j + 1;    --'IN' means Inches

      edi_846(j) := 'MEA*CT*NL*' || to_char(skid_rec.skid_pieces) || '*PC'; j := j + 1; --'PC' means pieces
      edi_846(j) := 'DTM*009*' || to_char(skid_rec.skid_date, 'YYYYMMDD*HHMI') || '*ET'; j := j + 1;

      edi_846(j) := 'QTY*01*1'; j := j + 1;

      li_skid_count := li_skid_count + 1;
   end loop;


   for coil_rec in coil_cursor loop
    --Coils data
    li_coil_status := coil_rec.coil_status;
    --edi_846(j) := 'LIN**PO*' || coil_rec.purchase_order_num || '*BP*' || coil_rec.part_num || '*IN*' || coil_rec.coil_org_num || '*VN*' || coil_rec.coil_org_num || '*PK*' || coil_rec.coil_mid_num || '*VO*' || coil_rec.supplier_sales_num; j := j + 1;
    case li_coil_status
      when 3 then         -- 3:Rejeced
           ls_coil_status := '2';
           edi_846(j) := 'LIN**PO*' || coil_rec.purchase_order_num || '*BP*' || coil_rec.part_num || '*IN*' || coil_rec.coil_abc_num || '*VN*' || coil_rec.coil_org_num || '*PK*' || coil_rec.coil_mid_num || '*VO*' || coil_rec.supplier_sales_num; j := j + 1;
      when 7 then       -- 7:Rebanded
           ls_coil_status := '7';
           edi_846(j) := 'LIN**PO*' || coil_rec.purchase_order_num || '*BP*' || coil_rec.part_num || '*IN*' || coil_rec.coil_abc_num || '*VN*' || coil_rec.coil_org_num || '*PK*' || coil_rec.coil_abc_num || '*VO*' || coil_rec.supplier_sales_num; j := j + 1;
      else
           ls_coil_status := 'Q';
           edi_846(j) := 'LIN**PO*' || coil_rec.purchase_order_num || '*BP*' || coil_rec.part_num || '*IN*' || coil_rec.coil_abc_num || '*VN*' || coil_rec.coil_org_num || '*PK*' || coil_rec.coil_mid_num || '*VO*' || coil_rec.supplier_sales_num; j := j + 1;
    end case;      -- 7: In process; Q: Not Scheduled; M: Return Material to Mill; 2: Hold for Inspection
    edi_846(j) := 'PID*S*MA*ST*' || ls_coil_status; j := j + 1;
    edi_846(j) := 'MEA*PD*N*' || coil_rec.net_wt_balance || '*LB'; j := j + 1;
    edi_846(j) := 'MEA*PD*G*' || coil_rec.net_wt_balance || '*LB'; j := j + 1;
    edi_846(j) := 'QTY*01*1'; j := j + 1;

    li_coil_count := li_coil_count + 1;
   end loop;



   --edi_846(j) := 'CTT*' || li_coil_count; j := j + 1;
   edi_846(j) := 'SE*' || to_char(j-1) || '*' || li_st_log; j := j + 1;
   edi_846(j) := 'GE*1*' || li_gs_log; j := j + 1;
   edi_846(j) := 'IEA*1*' || LPAD(to_char(li_gs_log),9,'0');

   if li_coil_count > 0 or li_skid_count > 0 then
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
    end if;


  return 1;

  EXCEPTION
     WHEN dup_val_on_index then
     rollback;
     return -1;
     WHEN UTL_FILE.INVALID_PATH THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN -2;
     WHEN UTL_FILE.INVALID_MODE THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN -3;
    WHEN UTL_FILE.INVALID_OPERATION THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN -4;
    WHEN UTL_FILE.WRITE_ERROR THEN
      UTL_FILE.FCLOSE_ALL;
      RETURN -5;
    WHEN OTHERS THEN
     UTL_FILE.FCLOSE_ALL;
      RETURN -6;


  return 1;
end f_846_cleveland_cliff;
