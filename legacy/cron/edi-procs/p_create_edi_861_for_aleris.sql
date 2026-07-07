  CREATE OR REPLACE PROCEDURE "DBO"."P_CREATE_EDI_861_FOR_ALERIS" IS
  /*
  This function creates ALCAN EDI861s for all edi_file_id/bol, which has not been sent yet but had all
  coils processed (either imported or marked damaged). It also sends the edi with a shipment received time.
  Then updates the inbound_shipment_status table.

  edi_file_id and bol are from inbound_shipment table, received_time is generated the current moment by the system.

  This function will be called from the cron job in the OS, runs periodically.
  */

  TYPE edi_861_tabletype IS TABLE OF VARCHAR2(32767) INDEX BY BINARY_INTEGER;
  edi_861 edi_861_tabletype;
  j	  BINARY_INTEGER;
  i	  INTEGER;
  edi_file_prefix VARCHAR2(50) := 'S_edi_';

  --collect all not-sent-yet record in inbound_shipment_status
  CURSOR not_sent_cursor IS
    SELECT ss.bol, ss.edi_file_id
      FROM inbound_shipment_status   ss,
	   inbound_shipment	     s,
	   inbound_shipment_customer c
     WHERE ss.edi_file_id = s.edi_file_id
       AND ss.bol = s.bol
       AND s.ship_from = c.ship_from
       AND c.customer_id = 1980
       AND -- customers for 861
	   ss.status = 3;
  /*cursor not_sent_cursor is
  select bol, edi_file_id
  from inbound_shipment_status
  where status = 3 ;*/

  CURSOR coil_status_cursor(par_bol VARCHAR2, par_edi_file_id INTEGER) IS
    SELECT status
      FROM inbound_coil_status
     WHERE bol = par_bol
       AND edi_file_id = par_edi_file_id;

  CURSOR bol_and_edi_file_id_cursor(par_bol	    VARCHAR2,
				    par_edi_file_id INTEGER) IS
    SELECT a.coil_number,
	   a.temper,
	   a.net_weight,
	   a.gross_weight,
	   a.lineal_feed,
	   a.coil_width,
	   a.coil_gauge,
	   a.density,
	   a.lot,
	   a.pack_id,
	   a.alloy,
	   a.ps_coil_number,
	   b.damaged_code,
	   b.damaged_fault
      FROM inbound_coil a, inbound_coil_status b
     WHERE a.bol = par_bol
       AND a.edi_file_id = par_edi_file_id
       AND a.bol = b.bol
       AND a.edi_file_id = b.edi_file_id
       AND a.item_num = b.item_num;

  li_coil_ready  INTEGER;
  li_gs_log	 INTEGER;
  li_st_log	 INTEGER;
  ls_today_short VARCHAR2(20);
  ls_today	 VARCHAR2(20);
  ls_now	 VARCHAR2(20);

  ld_received_time  DATE;
  ls_received_time  VARCHAR2(13);
  li_customer_id    INT;
  ls_po 	    VARCHAR2(20);
  ls_part_num	    VARCHAR2(40);
  ls_coil_number    VARCHAR2(40);
  ls_ps_coil_number VARCHAR2(40);
  ls_temper	    VARCHAR2(8);
  ln_net_weight     NUMBER(8, 0);
  ln_gross_weight   NUMBER(8, 0);
  ln_lineal_feed    NUMBER(7, 2);
  ln_coil_width     NUMBER(7, 2);
  ln_coil_gauge     NUMBER(5, 4);
  ln_density	    NUMBER;
  ls_lot	    VARCHAR2(40);
  ls_pack_id	    VARCHAR2(40);
  ls_alloy	    VARCHAR2(40);
  ln_damage_fault   NUMBER(1);
  ln_damage_code    NUMBER(3);

  edi_location	  VARCHAR2(50) := '/templar/templar/incoming/senddata';
  edi_file	  VARCHAR2(50) := 'edi_out.txt';
  edi_file_handle utl_file.file_type;
  li_edi_861_id   INTEGER;
  ls_dun	  VARCHAR2(32);
  ls_ship_from	  VARCHAR2(40);

  coil_count INTEGER;

  li_coil_abc_num INTEGER;

  li_count INTEGER;
BEGIN
  --create one EDI861 file for each selected edi_file_id/bol.
  FOR not_sent_cloop IN not_sent_cursor LOOP

    li_coil_ready := 0;

    --if not all coils are imported or mark damaged yet, don't send them
    FOR coil_status_cloop IN coil_status_cursor(not_sent_cloop.bol,
						not_sent_cloop.edi_file_id) LOOP
      IF (coil_status_cloop.status = 1 OR coil_status_cloop.status = 2) THEN
	--status 1:imported  2:marked damaged
	li_coil_ready := 1;
      ELSE
	li_coil_ready := 0;
      END IF;
      EXIT WHEN li_coil_ready = 0;
    END LOOP;

    coil_count := 0;
    j	       := 0;

    IF (li_coil_ready = 1) THEN
      SELECT edi_st_log_seq.nextval INTO li_st_log FROM dual;
      SELECT edi_gs_log_seq.nextval INTO li_gs_log FROM dual;
      ls_today_short := to_char(SYSDATE, 'yymmdd');
      ls_today	     := to_char(SYSDATE, 'yyyymmdd');
      ls_now	     := to_char(SYSDATE, 'hh24mi');

      SELECT po, part_number
	INTO ls_po, ls_part_num
	FROM inbound_shipment
       WHERE bol = not_sent_cloop.bol
	 AND edi_file_id = not_sent_cloop.edi_file_id;

      /*ld_received_time := sysdate;   --set the received time as current system time*/
      SELECT COUNT(*)
	INTO li_count
	FROM receiving_bol r
       WHERE r.bol = not_sent_cloop.bol;

      IF li_count = 1 THEN
	SELECT nvl(r.received_date, SYSDATE)
	  INTO ld_received_time
	  FROM receiving_bol r
	 WHERE r.bol = not_sent_cloop.bol;
      ELSE
	ld_received_time := SYSDATE;
      END IF;

      ls_received_time := to_char(ld_received_time, 'yyyymmdd*hh24mi'); --need this format in EDI861 file

      edi_861(j) := 'ISA*00*	      *00*	    *01*039630926T     *ZZ*964790856	  *' ||
		    ls_today_short || '*' || ls_now || '*U*00200*' ||
		    lpad(to_char(li_gs_log), 9, '0') || '*0*P*>';
      j := j + 1;
      edi_861(j) := 'GS*RC*039630926T*964790856*' || ls_today || '*' ||
		    ls_now || '*' || li_gs_log || '*X*004010';
      j := j + 1;
      edi_861(j) := 'ST*861*' || li_st_log;
      j := j + 1;
      edi_861(j) := 'BRA*' || not_sent_cloop.bol || '*' || ls_today ||
		    '*00*1*' || ls_now;
      j := j + 1;
      edi_861(j) := 'REF*BM*' || not_sent_cloop.bol;
      j := j + 1;
      edi_861(j) := 'DTM*050*' || ls_received_time || '*ED';
      j := j + 1;
      edi_861(j) := 'N1*OU**1*039630926';
      j := j + 1;
      edi_861(j) := 'N1*MF*Aleris*1*964790856';
      j := j + 1;

      SELECT ship_from
	INTO ls_ship_from
	FROM inbound_shipment
       WHERE edi_file_id = not_sent_cloop.edi_file_id
	 AND bol = not_sent_cloop.bol;
      SELECT customer_id
	INTO li_customer_id
	FROM inbound_shipment_customer
       WHERE ship_from = ls_ship_from;

      SELECT customer.customer_duns_number_string
	INTO ls_dun
	FROM customer
       WHERE customer.customer_id = li_customer_id;

      /*if li_customer_id = 1153 then
	 ls_dun := '241003755';--'Novelis Kingston'
      elsif  li_customer_id = 1459 then
	 ls_dun := '003980216';--'Novelis Oswego'
      else
	 ls_dun := '241003755';
      end if;*/

      edi_861(j) := 'N1*SU**1*' || ls_dun;
      j := j + 1;

      FOR cloop IN bol_and_edi_file_id_cursor(not_sent_cloop.bol,
					      not_sent_cloop.edi_file_id) LOOP
	ls_coil_number	  := cloop.coil_number;
	ls_temper	  := cloop.temper;
	ln_net_weight	  := cloop.net_weight;
	ln_gross_weight   := cloop.gross_weight;
	ln_lineal_feed	  := cloop.lineal_feed;
	ln_coil_width	  := cloop.coil_width;
	ln_coil_gauge	  := cloop.coil_gauge;
	ln_density	  := cloop.density;
	ls_lot		  := cloop.lot;
	ls_pack_id	  := cloop.pack_id;
	ls_alloy	  := cloop.alloy;
	ls_ps_coil_number := cloop.ps_coil_number;
	ln_damage_fault   := cloop.damaged_fault;
	ln_damage_code	  := cloop.damaged_code;

	IF (ls_ps_coil_number IS NULL) OR (length(ls_ps_coil_number) = 0) THEN
	  ls_ps_coil_number := ls_coil_number;
	END IF;

	edi_861(j) := 'RCD**1*CX';
	j := j + 1;
	edi_861(j) := 'LIN**VO*' || ls_po || '*BP*' || ls_part_num ||
		      '*HN*' || ls_lot || '*SN*' || ls_coil_number;
	j := j + 1;
	edi_861(j) := 'PID*S*MAC*ST*01';
	j := j + 1;
	edi_861(j) := 'PID*S*MA*ST*7';
	j := j + 1;

	--add these two segments only for damaged coils
	IF ln_damage_code <> 0 THEN
	  edi_861(j) := 'PID*S*DAC*ST*' || ln_damage_code;
	  j := j + 1;
	END IF;
	IF ln_damage_fault <> 0 THEN
	  /* DAF??? */
	  edi_861(j) := 'PID*S*DAF*ST*' || ln_damage_fault;
	  j := j + 1;
	END IF;

	edi_861(j) := 'PID*S*QAS*ST*2';
	j := j + 1;

	SELECT COUNT(*)
	  INTO li_count
	  FROM coil c
	 WHERE c.coil_org_num = ls_coil_number
	   AND c.coil_mid_num = ls_pack_id
	   AND c.customer_id = 1980;

	IF li_count = 1 THEN
	  SELECT nvl(c.coil_abc_num, 0)
	    INTO li_coil_abc_num
	    FROM coil c
	   WHERE c.coil_org_num = ls_coil_number
	     AND c.coil_mid_num = ls_pack_id
	     AND c.customer_id = 1980;
	ELSE
	  li_coil_abc_num := 0;
	END IF;

	edi_861(j) := 'REF*SE*' || to_char(li_coil_abc_num);
	j := j + 1;
	edi_861(j) := 'REF*RV*' || ls_ps_coil_number;
	j := j + 1;
	edi_861(j) := 'PRF*' || ls_po;
	j := j + 1;
	edi_861(j) := 'MEA*WT*WT*' || ln_net_weight || '*01';
	j := j + 1;
	edi_861(j) := 'MEA*WT**' || ln_gross_weight || '*24';
	j := j + 1;
	edi_861(j) := 'MEA*PD*TH*' || to_char(ln_coil_gauge, 'FM90.0000') ||
		      '*IN';
	j := j + 1;
	edi_861(j) := 'MEA*PD*WD*' ||
		      to_char(ln_coil_width, 'FM99990.0000') || '*IN';
	j := j + 1;
	edi_861(j) := 'MEA*CT**' || ln_lineal_feed || '*LF';
	j := j + 1;

	coil_count := coil_count + 1;
      END LOOP;

      edi_861(j) := 'CTT*' || coil_count;
      j := j + 1;
      edi_861(j) := 'SE*' || to_char(j - 1) || '*' || li_st_log;
      j := j + 1;
      edi_861(j) := 'GE*1*' || li_gs_log;
      j := j + 1;
      edi_861(j) := 'IEA*1*' || lpad(to_char(li_gs_log), 9, '0');

      IF coil_count > 0 THEN
	SELECT edi_file_id_seq.nextval INTO li_edi_861_id FROM dual;
-- Changed file suffix on 07/27/2016 to accomodate GXS change to SFTP - Patrick Reynolds
--	edi_file	:= 's_edi_' || to_char(li_edi_861_id) || '.861';
	edi_file	:= edi_file_prefix || to_char(li_edi_861_id) || '.edi';
	edi_file_handle := utl_file.fopen(edi_location, edi_file, 'W');
	--write edi to file
	FOR i IN 0 .. j LOOP
	  utl_file.put_line(edi_file_handle, edi_861(i));
	END LOOP;
	utl_file.fclose(edi_file_handle);

	/*--added by Victor Huang on 06/05 for function acknowlegement
	 INSERT INTO edi_fa(gs_num, duns_num, send_time,edi_file_name)
	 VALUES(li_gs_log, ls_dun, sysdate, edi_file);
	 COMMIT;
	*/
	--added by Victor Huang on 12/07 for verifying 997
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
	   SYSDATE,
	   edi_file,
	   0,
	   li_customer_id,
	   li_st_log,
	   '861');
	COMMIT;

	edi_file	:= 'send_edi_log';
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

	UPDATE inbound_shipment_status
	   SET status = 1
	 WHERE bol = not_sent_cloop.bol
	   AND edi_file_id = not_sent_cloop.edi_file_id;

	COMMIT;
      END IF;
    END IF;
  END LOOP; --loop end for edi_file_id/bol

  RETURN;

EXCEPTION
  WHEN dup_val_on_index THEN
    ROLLBACK;
    RETURN;
  WHEN utl_file.invalid_path THEN
    utl_file.fclose_all;
    RETURN;
  WHEN utl_file.invalid_mode THEN
    utl_file.fclose_all;
    RETURN;
  WHEN utl_file.invalid_operation THEN
    utl_file.fclose_all;
    RETURN;
  WHEN utl_file.write_error THEN
    utl_file.fclose_all;
    RETURN;
  WHEN OTHERS THEN
    utl_file.fclose_all;
    RETURN;

END p_create_edi_861_for_aleris;
/
