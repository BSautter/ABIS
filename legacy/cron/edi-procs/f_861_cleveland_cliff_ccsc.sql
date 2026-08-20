function f_861_cleveland_cliff_ccsc
(
   al_customer_id in customer.customer_id%TYPE,
   as_bol         in inbound_shipment.bol%TYPE
)

return integer
is

  FunctionResult              integer;

  /*
  This function creates Novelis EDI 861.
  edi_file_id and bol are from inbound_shipment table, received_time is generated the current moment by the system.
  This function will be called from Coil Receiving after a successful saving
  */

  type edi_861_tabletype is table of varchar2(32767) index by binary_integer;
  edi_861                     edi_861_tabletype;

  j                           binary_integer;
  i                           integer;

  edi_file_prefix             VARCHAR2(50) := 's_cliff_ccsc_861_';
  li_coil_ready               integer;
  li_gs_log                   integer;
  li_st_log                   integer;
  ls_today_short              varchar2(20);
  ls_today                    varchar2(20);
  ls_now                      varchar2(20);
  ld_received_time            date;
  ls_received_time            varchar2(13);
  ls_received_time_yy         varchar2(13);
  li_customer_id              integer;
  ls_vo                       varchar2(20);
  ls_coil_number              varchar2(40);
  ls_ps_coil_number           varchar2(40);
  ls_temper                   varchar2(8);
  ln_net_weight               number(8, 0);
  ln_gross_weight             number(8, 0);
  ln_lineal_feed              number(7, 2);
  ln_coil_width               number(7, 2);
  ln_coil_gauge               number(5, 4);
  ln_density                  number;
  ls_lot                      varchar2(40);
  ls_pack_id                  varchar2(40);
  ls_alloy                    varchar2(40);
  ln_damage_fault             number(1);
  ln_damage_code              number(3);
  --edi_location                varchar2(50) := '/templar/templar/incoming/senddata';
  edi_location                VARCHAR2(50) := dbo.get_edi_destination('EDI_DIR');
  edi_file                    varchar2(50) := 'edi_out.txt';
  edi_file_handle             utl_file.file_type;
  li_edi_861_id               integer;
  ls_dun                      varchar2(32);
  ls_ship_from                varchar2(40);
  coil_count                  integer;
  li_coil_abc_num             integer;
  li_count                    integer;
  ls_received_date            varchar2(8);
  li_inbound_shipment_status  inbound_shipment_status.status%TYPE;
  ll_edi_file_id              inbound_shipment_status.edi_file_id%TYPE;
  is_create_861_at_receiving  char(1);
  ls_po                       receiving_bol_coil.purchase_order_num%TYPE;
  ls_customer_short_name      customer.customer_short_name%TYPE;
  ll_pos                      integer;

  --Table data_861 is populated in function wf_populate_data_861 for w_coil_receiving in coil_receiving.pbl
  cursor data_861_cursor is
  select    customer_id,
            bol,
            edi_file_id,
            coil_org_num,
            receiving_bol_id,
            coil_abc_num,
            coil_status,
            damaged_fault,
            damaged_code,
            temper,
            net_weight,
            gross_weight,
            lineal_feed,
            coil_width,
            coil_gauge,
            density,
            lot,
            pack_id,
            alloy,
            part_num,
            supplier_sales_num,
            consumed_coil_num,
            material_num,
            cash_date,
            coil_from_cust_id,
            created_date,
            received_date,
            po,
            po_line_num,
            customer_duns_number,
            mill_duns_number,
            packaging_code,
            loading_qty
  from      dbo.data_861
  where     customer_id = al_customer_id
  and       bol = as_bol
  and       edi_file_id = ll_edi_file_id
  order by  edi_file_id;

  cursor edi_file_id_cursor(as_bol varchar2) is
     select  distinct edi_file_id
     from    dbo.data_861
     where   customer_id = al_customer_id
      and    bol = as_bol;

begin

    --******************************************************************************************
    --Alex Gerlants. 10/08/2019, 09:30 AM. Activated this function by commenting out the next line
    --return 1; --********************** Disable this function pending Novelis approval **********
    --******************************************************************************************

/*
    --This is already checked in wf_create_861_4customer for w_coil_receiving
    select	upper(nvl(create_861_at_receiving, 'N'))
    into		is_create_861_at_receiving
    from		customer
    where		customer_id = al_customer_id;

    if is_create_861_at_receiving <> 'Y' then
      return 2; --Nothing to do here
    end if;
*/
    --Alex Gerlants. 03/09/2021. Added '2950' to the line below
    if not ( al_customer_id in (1153, 1459, 2582, 2950) ) then
      return 3; --Wrong customer
    end if;

    FunctionResult := 1;

    for edi_file_id_cloop in edi_file_id_cursor(as_bol) loop

        coil_count := 0;
        j          := 0;

        ll_edi_file_id := edi_file_id_cloop.edi_file_id;

        select    distinct ps_coil_number
        into      ls_ps_coil_number
        from      inbound_coil
        where     bol = as_bol
        and       edi_file_id = ll_edi_file_id;

      --IF (li_coil_ready = 1) THEN
        SELECT edi_st_log_seq.nextval INTO li_st_log FROM dual;
        SELECT edi_gs_log_seq.nextval INTO li_gs_log FROM dual;
        ls_today_short := to_char(SYSDATE, 'yymmdd');
        ls_today       := to_char(SYSDATE, 'yyyymmdd');
        ls_now         := to_char(SYSDATE, 'hh24mi');

        SELECT distinct vo
          INTO ls_vo
          FROM inbound_shipment
         WHERE bol = as_bol
         and   edi_file_id = ll_edi_file_id;

        /*ld_received_time := sysdate;   --set the received time as current system time*/
        SELECT COUNT(*)
          INTO li_count
          FROM receiving_bol
         WHERE bol = as_bol;

        IF li_count = 1 THEN
          SELECT distinct nvl(created_date, sysdate)
            INTO ld_received_time
            FROM receiving_bol
           WHERE bol = as_bol;
        ELSE
          ld_received_time := SYSDATE;
        END IF;

        SELECT distinct ship_from
          INTO ls_ship_from
          FROM inbound_shipment
         WHERE bol = as_bol;

        SELECT distinct customer_id
          INTO li_customer_id
          FROM inbound_shipment_customer
         WHERE ship_from = ls_ship_from;

        SELECT customer.customer_duns_number_string
          INTO ls_dun
          FROM customer
         WHERE customer.customer_id = al_customer_id;

        ls_received_time := to_char(ld_received_time, 'yyyymmdd*hh24mi'); --need this format in EDI861 file
        ls_received_time_yy := to_char(ld_received_time, 'yymmdd*hh24mi');

        --Production
        edi_861(j) := 'ISA*00*' ||
                       '          *00*          *01*039630926T     *09*0015049350011G *' ||
                        ls_received_time_yy || '*U*00401*' ||
                        lpad(to_char(li_gs_log), 9, '0') || '*0*P**';

        --Development
        --edi_861(j) := 'ISA*00*' ||
        --                '          *00*          *ZZ*2NDSFTP        *ZZ*NOVLSTEST      *' ||
        --                ls_received_time_yy || '*U*00401*' ||
        --                lpad(to_char(li_gs_log), 9, '0') || '*0*T**';

        j := j + 1;

        --Production
        edi_861(j) := 'GS*SH*R0P7A*' || '001504935001' || '*' || ls_today || '*' ||
                        ls_now || '*' || to_char(li_gs_log) || '*X*' || '004010';

        --Development
        --edi_861(j) := 'GS*SH*2NDSFTP*' || 'NOVLSTEST' || '*' || ls_today || '*' ||
        --               ls_now || '*' || to_char(li_gs_log) || '*X*' || '004010';

        j := j + 1;

        edi_861(j) := 'ST*861*' || li_st_log;
        j := j + 1;

        edi_861(j) := 'BRA*' || as_bol || '*' || ls_today ||
                      '*00*1*' || ls_now;
        j := j + 1;

        edi_861(j) := 'REF*BM*' || as_bol;
        j := j + 1;

        edi_861(j) := 'DTM*050*' || ls_received_time || '*ED';
        j := j + 1;

        select customer.customer_short_name
          into ls_customer_short_name
          from customer
         where customer.customer_id = li_customer_id;

        edi_861(j) := 'N1*MF*' || ltrim(rtrim(ls_customer_short_name)) || '*1*' || ls_dun;
        j := j + 1;

        edi_861(j) := 'N1*OU*' || 'ALUMINUM BLANKING CO., INC.' || '*1*039630926'; --ALBL DUNS
        j := j + 1;

        edi_861(j) := 'N1*SU*' || ltrim(rtrim(ls_customer_short_name)) || '*1*' || ls_dun; --Novelis DUNS
        j := j + 1;

        for cloop in data_861_cursor() loop
          ls_coil_number    := cloop.coil_org_num;
          li_coil_abc_num   := cloop.coil_abc_num;
          ls_temper         := cloop.temper;
          ln_net_weight     := cloop.net_weight;
          ln_gross_weight   := cloop.gross_weight;
          ln_lineal_feed    := cloop.lineal_feed;
          ln_coil_width     := cloop.coil_width;
          ln_coil_gauge     := cloop.coil_gauge;
          ln_density        := cloop.density;
          ls_lot            := cloop.lot;
          ls_pack_id        := cloop.pack_id;
          ls_alloy          := cloop.alloy;
          ln_damage_fault   := cloop.damaged_fault;
          ln_damage_code    := cloop.damaged_code;
          ls_po             := cloop.po;

          IF (ls_ps_coil_number IS NULL) OR (length(ls_ps_coil_number) = 0) THEN
            ls_ps_coil_number := ls_coil_number;
          END IF;

          edi_861(j) := 'RCD**1*CX';
          j := j + 1;

          ll_pos := instr(ls_po, '-', 1);

          if ll_pos > 0 then
            ls_po := substr(ls_po, 1, ll_pos - 1);
          end if;

          edi_861(j) := 'LIN**VO*' || ls_po || '*SN*' || ls_coil_number || '*HN*' || ls_lot;

          j := j + 1;

          edi_861(j) := 'PID*S*MAC*ST*01***67';
          j := j + 1;

          edi_861(j) := 'PID*S*MA*ST*7***70';
          j := j + 1;


          --add these two segments only for damaged coils

          IF ln_damage_code <> 0 THEN
            edi_861(j) := 'PID*S*DAC*ST*' || ln_damage_code;
            j := j + 1;
          END IF;

          IF ln_damage_fault <> 0 THEN
            edi_861(j) := 'PID*S*DAF*ST*' || ln_damage_fault;
            j := j + 1;
          END IF;

          edi_861(j) := 'REF*SE*' || li_coil_abc_num;
          j := j + 1;

          ls_received_date := to_char(ld_received_time, 'yyyymmdd');

          if (ls_ps_coil_number is null) or (length(ls_ps_coil_number) = 0) then
            ls_ps_coil_number := ls_coil_number;
          end if;

          edi_861(j) := 'REF*RV*' || ls_ps_coil_number;
          j := j + 1;

          edi_861(j) := 'MEA*WT*N*' || ln_net_weight || '*01';
          j := j + 1;

          edi_861(j) := 'MEA*WT*G*' || ln_gross_weight || '*24';
          j := j + 1;

          edi_861(j) := 'MEA*PD*TH*' || to_char(ln_coil_gauge, 'FM90.0000') || '*IN';
          j := j + 1;

          edi_861(j) := 'MEA*PD*WD*' || to_char(ln_coil_width, 'FM99990.0000') || '*IN';
          j := j + 1;

          if not ln_lineal_feed is null then --Alex Gerlants. 12/02/2019. Email from Novelis 11/21/2019 @04:40PM
            edi_861(j) := 'MEA*PD*LN*' || ln_lineal_feed || '*LF';
            j := j + 1;
          end if;

          coil_count := coil_count + 1;

          update    inbound_coil_status
          set       coil_abc_num = li_coil_abc_num
          where     bol = as_bol
          and       edi_file_id = ll_edi_file_id
          and       coil_number = ls_coil_number;
        END LOOP; --for cloop in data_861_cursor() loop

        edi_861(j) := 'CTT*' || coil_count;
        j := j + 1;
        edi_861(j) := 'SE*' || to_char(j - 1) || '*' || li_st_log;
        j := j + 1;
        edi_861(j) := 'GE*1*' || li_gs_log;
        j := j + 1;
        edi_861(j) := 'IEA*1*' || lpad(to_char(li_gs_log), 9, '0');

        IF coil_count > 0 THEN
            SELECT edi_file_id_seq.nextval INTO li_edi_861_id FROM dual;

            edi_file        := edi_file_prefix || to_char(li_edi_861_id) || '.edi';
            edi_file_handle := utl_file.fopen(edi_location, edi_file, 'W');
            --write edi to file
            FOR i IN 0 .. j LOOP
              utl_file.put_line(edi_file_handle, edi_861(i));
            END LOOP;
            utl_file.fclose(edi_file_handle);
          --END IF;

        update inbound_shipment_status
          set    status = 1,
                 filename_861 = edi_file,
                 created_861_date_time = sysdate
          where  bol = as_bol
          and    edi_file_id = ll_edi_file_id;

          INSERT INTO outbound_edi_transaction
            (edi_file_id,
             duns_from,
             duns_to,
             interchange_control_number,
             group_control_number,
             transaction_time,
             edi_file_name,
             fa_receive_status,
             customer_id,
             set_control_num,
             transaction_type_id)
          VALUES
            (li_edi_861_id,
             '039630926',
             ls_dun,
             li_gs_log,
             li_gs_log,
             sysdate,
             edi_file,
             0,
             li_customer_id,
             li_st_log,
             '861');

          commit;


          edi_file        := 'send_edi_log';
          edi_file_handle := utl_file.fopen(edi_location, edi_file, 'a');
          utl_file.put_line(edi_file_handle, 'Type: 861');
          utl_file.put_line(edi_file_handle,
                            'Sending Time: ' ||
                            to_char(SYSDATE, 'MM/DD/YY HH24:MI'));
          utl_file.put_line(edi_file_handle,
                            'Interchange Control #: ' || li_gs_log);
          utl_file.put_line(edi_file_handle,
                            'Group Control #: ' || li_gs_log);
          utl_file.put_line(edi_file_handle, 'Set Control #: ' || li_st_log);
          utl_file.put_line(edi_file_handle, '');
          utl_file.put_line(edi_file_handle, '');
          utl_file.fclose(edi_file_handle);

        END IF;
    end loop; --for edi_file_id_cloop in coil_status_cursor(as_bol) loop

  return(FunctionResult);

end f_861_cleveland_cliff_ccsc;
