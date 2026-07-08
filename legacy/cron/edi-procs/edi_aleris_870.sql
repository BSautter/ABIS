  CREATE OR REPLACE PROCEDURE "DBO"."EDI_ALERIS_870" IS

  /*
   Aluminum Blanking: DUNS:039630926	 EDI ID:039630926T	  QUALIFIER: 01
   Customer: Aleris(1980) DUNS:964790856     EDI ID:964790856	   QUALIFIER: ZZ
   This version of the stored procedure was copied from production on
   May 23, 2016 by Patrick Reynolds.
   The only main difference between the DEV and PROD version of the stored prcedure is that
   the DEV version had the update section all commented out.
   There was also an additional index (p) variable in the PROD code
   that the DEV version did not have.
   End User: Aleris, Customer ID 1980
   */

  --Alex Gerlants. 11/15/2016. Commented out, and added the line after.
  --edi_loc VARCHAR2(50) := '/templar/templar/incoming/senddata';
  edi_loc VARCHAR2(50) := dbo.get_edi_destination('EDI_DIR');

  edi_file	  VARCHAR2(50) := 'edi_out.txt';
  edi_file_prefix VARCHAR2(50) := 'S_aleris_';
  edi_file_handle utl_file.file_type;
  su_duns	  VARCHAR2(10);
  ll_customer_id  INTEGER := 1980;
  li_scrap_job_done  INTEGER := 2;

  ll_cur_job ab_job.ab_job_num%TYPE;

  ll_gs_log INTEGER;
  ll_st_log INTEGER;

  ls_today   VARCHAR2(20) := to_char(SYSDATE, 'YYYYMMDD');
  ls_today_4 VARCHAR2(20) := to_char(SYSDATE, 'YYMMDD');
  ls_now     VARCHAR2(20) := to_char(SYSDATE, 'HH24MI');

  ls_po VARCHAR2(32);
   ls_enduser_po VARCHAR2(32);
   ls_enduser_item_po VARCHAR2(32);
  ld_scrap_870_date ab_job.scrap_870_date%TYPE;

  ls_coil_org_num coil.coil_org_num%TYPE;
  ls_heat coil.lot_num%TYPE;
  ln_coil_thickness coil.coil_gauge%TYPE;
  ln_width  rectangle.rt_width%TYPE;
  ln_length rectangle.rt_length%TYPE;

  ll_ref_order_abc_num sheet_skid.ref_order_abc_num%TYPE;
  ll_ref_order_abc_item sheet_skid.ref_order_abc_item%TYPE;
  ls_sheet_type order_item.sheet_type%TYPE;

  ll_prime_nw coil.net_wt%TYPE;
  ll_scrap_nw coil.net_wt%TYPE;
  -- ll_process_quantity  coil.net_wt%TYPE;
  -- ll_process_end_wt	coil.net_wt%TYPE;
  lr_theo_unit_wt order_item.theoretical_unit_wt%TYPE;

  --ll_edi_file_id integer;
  ll_edi_file_id_aleris INTEGER;

  ll_item_count INTEGER;
  ll_job_count INTEGER;

  li_hl01 INTEGER;
  li_hllin INTEGER;


  CURSOR cust_cur IS
    SELECT customer_id FROM customer WHERE customer_id IN (1980); ---Add customer id here if you want to send 870 for more customers

  CURSOR job_cur IS
   SELECT UNIQUE production_sheet_item.ab_job_num
    FROM sheet_skid,
	   production_sheet_item,
	   sheet_skid_detail,
	   coil,
	   customer_order
     WHERE (sheet_skid.skid_sheet_status IN (2, 8))
       AND (production_sheet_item.prod_item_edi870_date IS NULL)
       AND (sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num)
       AND (sheet_skid_detail.prod_item_num =
	   production_sheet_item.prod_item_num)
       AND (coil.coil_abc_num = production_sheet_item.coil_abc_num)
       AND (coil.customer_id = 1980)
       AND (sheet_skid.ref_order_abc_num = customer_order.order_abc_num)
   UNION
       SELECT UNIQUE a.ab_job_num
       FROM ab_job a, customer_order b
       WHERE (a.order_abc_num = b.order_abc_num) and
	     (a.job_status = 0) and
	     (b.orig_customer_id = 1980) and
	     (a.scrap_870_date is null);


  CURSOR aleris_870_cur IS
    SELECT production_sheet_item.ab_job_num,
	   production_sheet_item.coil_abc_num,
	   production_sheet_item.prod_item_pieces,
	   production_sheet_item.prod_item_num,
	   sheet_skid.sheet_skid_num,
	   production_sheet_item.prod_item_net_wt,
	   sheet_skid.skid_sheet_status,
	   sheet_skid.sheet_net_wt,
	   sheet_skid.skid_pieces,
	   coil.coil_org_num,
	   coil.lot_num,
	   nvl(customer_order.sales_order, ' ') sales_order,
	   nvl(customer_order.orig_customer_po, 'NA') customer_po,
	   nvl(customer_order.enduser_po, 'NA') enduser_po,
	   nvl(order_item.enduser_part_num, ' ') enduser_parts,
	   (sheet_skid.sheet_net_wt + sheet_skid.sheet_tare_wt) skid_gross_wt,
	   NULL done
      FROM sheet_skid,
	   production_sheet_item,
	   sheet_skid_detail,
	   coil,
	   order_item,
			     ab_job,
	   customer_order
     WHERE (sheet_skid.skid_sheet_status IN (2, 4, 7, 8, 13)) -- 2: Ready; 8: Warehouse Ready; 4: onhold 13: Partial Ready
       AND (production_sheet_item.prod_item_edi870_date IS NULL)
       AND (sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num)
       AND (sheet_skid_detail.prod_item_num =
	   production_sheet_item.prod_item_num)
       AND (coil.coil_abc_num = production_sheet_item.coil_abc_num)
       AND (coil.customer_id = ll_customer_id)
       AND (sheet_skid.ref_order_abc_num = customer_order.order_abc_num)
 		   AND (production_sheet_item.ab_job_num = ab_job.ab_job_num)
		   AND (ab_job.order_abc_num = order_item.order_abc_num)
		   AND (ab_job.order_item_num = order_item.order_item_num)
      AND (production_sheet_item.ab_job_num = ll_cur_job);

      CURSOR aleris_scrap_870_cur IS
       SELECT coil_abc_num,
	       NVL(process_quantity, 0) process_quan,
	       NVL(process_end_wt, 0) process_end_wt
       FROM process_coil
       WHERE (process_coil.ab_job_num = ll_cur_job);

  TYPE store_tabletype IS TABLE OF VARCHAR2(32767) INDEX BY BINARY_INTEGER;
  x12_table store_tabletype;
  i	    BINARY_INTEGER := 0;
  item_rec  aleris_870_cur%ROWTYPE;

  TYPE prod_item_type IS TABLE OF production_sheet_item.prod_item_num%TYPE INDEX BY BINARY_INTEGER;
  item_num prod_item_type;
  k	   BINARY_INTEGER := 0;

  TYPE job_num_type IS TABLE OF ab_job.ab_job_num%type INDEX BY BINARY_INTEGER;
  job_num job_num_type;
  p binary_integer :=0;

BEGIN

  FOR cust_rec IN cust_cur LOOP
    --- Outer loop of customer_id
    ll_customer_id := cust_rec.customer_id;
    i		   := 0;
    k		   := 0;
    p		   := 0;

    SELECT COUNT(*)
      INTO ll_item_count
      FROM sheet_skid,
	   production_sheet_item,
	   sheet_skid_detail,
	   coil,
	   customer_order
     WHERE (sheet_skid.skid_sheet_status IN (2, 8)) -- 2: Ready; 8: Warehouse Ready; 13: Partial Ready
       AND (production_sheet_item.prod_item_edi870_date IS NULL)
       AND (sheet_skid.sheet_skid_num = sheet_skid_detail.sheet_skid_num)
       AND (sheet_skid_detail.prod_item_num =
	   production_sheet_item.prod_item_num)
       AND (coil.coil_abc_num = production_sheet_item.coil_abc_num)
       AND (coil.customer_id = ll_customer_id)
       AND (sheet_skid.ref_order_abc_num = customer_order.order_abc_num);

       SELECT count(a.ab_job_num)
       INTO ll_job_count
       FROM ab_job a, customer_order b
       WHERE (a.order_abc_num = b.order_abc_num) and
	     (a.job_status = 0) and
	     (b.orig_customer_id = 1980) and
	     (a.scrap_870_date is null);


    IF (ll_item_count <> 0) or (ll_job_count <> 0)THEN

      SELECT edi_st_log_seq.nextval INTO ll_st_log FROM dual;
      SELECT edi_gs_log_seq.nextval INTO ll_gs_log FROM dual;

      SELECT customer.customer_duns_number_string
	INTO su_duns
	FROM customer
       WHERE customer.customer_id = ll_customer_id;

      x12_table(i) := 'ISA*00*' ||
		      ' 	 *00*	       *01*039630926T	  *ZZ*964790856      *' ||
		      ls_today_4 || '*' || ls_now || '*U*00401*' ||
		      lpad(to_char(ll_gs_log), 9, '0') || '*0*P*>';
      i := i + 1;
      x12_table(i) := 'GS*RS*039630926T*' || '964790856' || '*' || ls_today || '*' ||
		      ls_now || '*' || to_char(ll_gs_log) || '*X*' ||
		      '004010';
      i := i + 1;
      x12_table(i) := 'ST*870*' || to_char(ll_st_log);
      i := i + 1;
      x12_table(i) := 'BSR*2*PP*' || to_char(ll_st_log) || '*' || ls_today ||
		      '***' || ls_now || '*****';
      i := i + 1;

      x12_table(i) := 'N1*OU*ALUMINUM BLANKING/MI*1*039630926T';
      i := i + 1;

       x12_table(i) := 'N1*MF**1*' || su_duns;
      i := i + 1;

      li_hl01 := 0;


      FOR job_rec IN job_cur LOOP    -- job loop


	  ll_cur_job := job_rec.ab_job_num;

	  job_num(p) := ll_cur_job;
	  p := p + 1;

	     SELECT nvl(customer_order.orig_customer_po, 'NA'), nvl(customer_order.enduser_po, 'NA')
	      INTO ls_po, ls_enduser_po
	      FROM ab_job, customer_order
	      WHERE (ab_job.ab_job_num = ll_cur_job)
	      and (ab_job.order_abc_num = customer_order.order_abc_num);


	li_hl01 := li_hl01 + 1;

	x12_table(i) := 'HL*' || to_char(li_hl01) || '**O*1';
	i := i + 1;

	x12_table(i) := 'PRF*' || ls_enduser_po || '***' || ls_today;
	i := i + 1;

	li_hl01 := li_hl01 + 1;

	 x12_table(i) := 'HL*' || to_char(li_hl01) || '*1*I*1';
	i := i + 1;

	x12_table(i) := 'PRF*RV*300578504';
	i := i + 1;

	li_hllin := li_hl01;



      FOR item_rec IN aleris_870_cur LOOP  -- item loop


	li_hl01 := li_hl01 + 1;

	x12_table(i) := 'HL*' || to_char(li_hl01) || '*' || to_char(li_hllin) || '*F';
	i := i + 1;

	ls_po := item_rec.customer_po;
	ls_enduser_item_po := item_rec.enduser_po;

	x12_table(i) := 'PRF*' || ls_enduser_item_po || '***' || ls_today;
	i := i + 1;

	x12_table(i) := 'REF*SE*' || item_rec.sheet_skid_num;
	 i := i + 1;

	x12_table(i) := 'DTM*009*' || ls_today || '*' || ls_now || '*ES';
	i := i + 1;

	x12_table(i) := 'DTM*206*' || ls_today;
	i := i + 1;


	x12_table(i) := 'PO1**1*UN***VO*' || ls_enduser_item_po ||
			  '*SN*' || item_rec.coil_org_num || '*HN*' || item_rec.lot_num || '***BP*' || item_rec.enduser_parts;
	 i := i + 1;


	 x12_table(i) := 'PID*S*MAC*ST*01***67';
	  i := i + 1;
	 x12_table(i) := 'PID*S*22*ST*20***22';
	  i := i + 1;

       IF item_rec.skid_sheet_status = 2 THEN
	  x12_table(i) := 'PID*S*MA*ST*1***70' ;
	  i := i + 1;
	ELSIF item_rec.skid_sheet_status = 13 THEN
	  x12_table(i) := 'PID*S*MA*ST*8***70';
	  i := i + 1;
	ELSIF item_rec.skid_sheet_status = 4 THEN
	  x12_table(i) := 'PID*S*MA*ST*6***70';
	  i := i + 1;
	ELSE
	  x12_table(i) := 'PID*S*MA*ST*3***70';
	  i := i + 1;
	END IF;

	x12_table(i) := 'PID*S*PR*ST*19***66';
	  i := i + 1;


    SELECT MAX(coil_gauge)
      INTO ln_coil_thickness
      FROM coil
     WHERE coil.coil_abc_num IN
	   (SELECT coil_abc_num
	      FROM production_sheet_item
	     WHERE production_sheet_item.prod_item_num IN
		   (SELECT prod_item_num
		      FROM sheet_skid_detail
		     WHERE sheet_skid_detail.sheet_skid_num =
			   item_rec.sheet_skid_num));

    x12_table(i) := 'MEA*PD*TH*' ||
		    TRIM(to_char(ln_coil_thickness, '99999.99')) || '*ED';
    i := i + 1;
    x12_table(i) := 'MEA*PD*TH*' ||
		    TRIM(to_char(ln_coil_thickness * 25.4, '99999.99')) ||
		    '*MB';
    i := i + 1;

    SELECT ss.ref_order_abc_num, ss.ref_order_abc_item
      INTO ll_ref_order_abc_num, ll_ref_order_abc_item
      FROM sheet_skid ss
     WHERE ss.sheet_skid_num = item_rec.sheet_skid_num;

    SELECT sheet_type, NVL(theoretical_unit_wt, 0)
      INTO ls_sheet_type, lr_theo_unit_wt
      FROM order_item
     WHERE order_abc_num = ll_ref_order_abc_num
       AND order_item_num = ll_ref_order_abc_item;

    CASE TRIM(upper(ls_sheet_type))
      WHEN 'RECTANGLE' THEN
	SELECT t.rt_length, t.rt_width
	  INTO ln_length, ln_width
	  FROM rectangle t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'PARALLELOGRAM' THEN
	SELECT t.p_length, t.p_width
	  INTO ln_length, ln_width
	  FROM parallelogram t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'FENDER' THEN
	SELECT t.fe_length, t.fe_side
	  INTO ln_length, ln_width
	  FROM fender t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'CHEVRON' THEN
	SELECT t.ch_length, t.ch_width
	  INTO ln_length, ln_width
	  FROM chevron t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'CIRCLE' THEN
	SELECT t.c_diameter, t.c_diameter
	  INTO ln_length, ln_width
	  FROM circle t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'TRAPEZOID' THEN
	SELECT t.tr_long_length, t.tr_width
	  INTO ln_length, ln_width
	  FROM trapezoid t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'L.TRAPEZOID' THEN
	SELECT t.ltr_long_length, t.ltr_width
	  INTO ln_length, ln_width
	  FROM left_trapezoid t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'R.TRAPEZOID' THEN
	SELECT t.rtr_long_length, t.rtr_width
	  INTO ln_length, ln_width
	  FROM right_trapezoid t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'REINFORCEMENT' THEN
	SELECT t.re_length, t.re_width
	  INTO ln_length, ln_width
	  FROM reinforcement t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      WHEN 'LIFTGATE' THEN
	SELECT t.li_length, t.li_width
	  INTO ln_length, ln_width
	  FROM liftgate_shape t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
      ELSE
	SELECT t.x_1, t.x_2
	  INTO ln_length, ln_width
	  FROM x1_shape t
	 WHERE t.order_abc_num = ll_ref_order_abc_num
	   AND t.order_item_num = ll_ref_order_abc_item;
    END CASE;

    x12_table(i) := 'MEA*PD*WD*' || TRIM(to_char(ln_width, '99999.99')) ||
		    '*ED';
    i := i + 1;

    x12_table(i) := 'MEA*PD*WD*' ||
		    TRIM(to_char(ln_width * 25.4, '99999.99')) || '*MB';
    i := i + 1;

    x12_table(i) := 'MEA*CT*LN*' || TRIM(to_char(ln_length, '99999.99')) ||
		    '*ED';
    i := i + 1;

    x12_table(i) := 'MEA*PD*LN*' ||
		    TRIM(to_char(ln_length * 25.4, '99999.99')) || '*MB'; -- Linear MillMeter
    i := i + 1;

   x12_table(i) := 'MEA*CT*NL*' || to_char(item_rec.prod_item_pieces) ||
		    '*PC';
    i := i + 1;

    x12_table(i) := 'MEA*WT*WT*' || to_char(item_rec.prod_item_pieces * lr_theo_unit_wt, '99999') ||
		    '*24';
    i := i + 1;

    x12_table(i) := 'MEA*WT*WT*' || to_char(item_rec.prod_item_pieces * lr_theo_unit_wt * 0.4536, '99999') || '*53';
    i := i + 1;

    x12_table(i) := 'MEA*WT*WT*' || to_char(item_rec.prod_item_net_wt) ||
		    '*01';
    i := i + 1;

    x12_table(i) := 'MEA*WT*WT*' ||
		    to_char(item_rec.prod_item_net_wt * 0.4536, '99999') || '*50';
    i := i + 1;



 --	  x12_table(i) := 'MEA*WT*G*' || to_char(item_rec.skid_gross_wt) ||'*LB*01';
 --	  i := i + 1;
 --	  x12_table(i) := 'MEA*CT*NA*' || to_char(item_rec.prod_item_pieces) || '*PC';
--	  i := i + 1;
	item_num(k) := item_rec.prod_item_num;
	k := k + 1;

      END LOOP;  -- item loop

     SELECT scrap_870_date, job_status
     INTO ld_scrap_870_date, li_scrap_job_done
     FROM ab_job
     WHERE ab_job_num = ll_cur_job;

     IF (ld_scrap_870_date is NULL) AND (li_scrap_job_done = 0) THEN  -- scrap not sent and job is done

      FOR scrap_rec IN aleris_scrap_870_cur LOOP    -- scrap loop

	--scrap info

	SELECT coil_org_num, lot_num
	INTO ls_coil_org_num, ls_heat
	FROM coil
	WHERE coil_abc_num = scrap_rec.coil_abc_num;

	SELECT NVL(sum(production_sheet_item.prod_item_net_wt),0)
	into ll_prime_nw
	 FROM production_sheet_item
	 WHERE (production_sheet_item.ab_job_num = ll_cur_job) and
	       (production_sheet_item.coil_abc_num = scrap_rec.coil_abc_num);


		ll_scrap_nw := scrap_rec.process_quan - scrap_rec.process_end_wt - ll_prime_nw;



		IF ll_scrap_nw > 0 then

		   li_hl01 := li_hl01 + 1;

		   x12_table(i) := 'HL*' || to_char(li_hl01) || '*' || to_char(li_hllin) || '*F';
		   i := i + 1;

		   x12_table(i) := 'PRF*' || ls_enduser_po || '***' || ls_today;
		   i := i +1;

		    x12_table(i) := 'PO1**1*UN***VO*' || ls_enduser_po ||
			  '*SN*' || ls_coil_org_num || '*HN*' || ls_heat || '***BP* ';
		     i := i + 1;

		    x12_table(i) := 'PID*S*MAC*ST*05***67';
		    i := i + 1;
		    x12_table(i) := 'PID*S*DAF*ST*3***72';
		     i := i + 1;
		    x12_table(i) := 'PID*S*DAC*ST*258***73';
		     i := i + 1;

		   x12_table(i) := 'MEA*WT*WT*' || to_char(ll_scrap_nw) || '*01';
		   i := i+1;
		   x12_table(i) := 'MEA*WT*WT*' || to_char(ll_scrap_nw * 0.4536, '99999') || '*50';
		   i := i+1;
		 END IF;

	END LOOP;  -- scrap loop
      END IF;  -- scrap not sent


     END LOOP;	 -- job loop

      x12_table(i) := 'CTT*' || to_char(li_hl01);
      i := i + 1;
      x12_table(i) := 'SE*' || to_char(i - 1) || '*' || to_char(ll_st_log);
      i := i + 1;
      x12_table(i) := 'GE*1*' || to_char(ll_gs_log);
      i := i + 1;
      x12_table(i) := 'IEA*1*' || lpad(to_char(ll_gs_log), 9, '0');

      -- Write EDI to file

      IF li_hl01 > 1 THEN
	-- WRITE EDI FILE ONLY IF ITEM > 0
	SELECT edi_file_id_seq.nextval
	  INTO ll_edi_file_id_aleris
	  FROM dual;
-- Changed file suffix on 07/27/2016 to accomodate GXS change to SFTP - Patrick Reynolds
--	  edi_file	  := 's_aleris_' || to_char(ll_edi_file_id_aleris) ||
--			     '.870';
	 edi_file	 := edi_file_prefix || to_char(ll_edi_file_id_aleris) || '.edi';
	edi_file_handle := utl_file.fopen(edi_loc, edi_file, 'W');

	FOR j IN 0 .. i LOOP
	  utl_file.put_line(edi_file_handle, x12_table(j));

	END LOOP;
	utl_file.fclose(edi_file_handle);
      END IF; -- END WRITING EDI FILE

     --edi_file        := 'send_edi_log'; /* Alex Gerlants. 04/14/2017. Commented out. */
      edi_file_handle := utl_file.fopen(edi_loc, edi_file, 'a');
      utl_file.put_line(edi_file_handle, 'Type: 870');
      utl_file.put_line(edi_file_handle,
			'Sending Time: ' ||
			to_char(SYSDATE, 'MM/DD/YY HH24:MI'));
      utl_file.put_line(edi_file_handle,
			'Interchange Control #: ' || ll_gs_log);
      utl_file.put_line(edi_file_handle, 'Group Control #: ' || ll_gs_log);
      utl_file.put_line(edi_file_handle, 'Set Control #: ' || ll_st_log);
      utl_file.put_line(edi_file_handle, '');
      utl_file.put_line(edi_file_handle, '');
      utl_file.fclose(edi_file_handle);

      --For verifying 997
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
	  (ll_edi_file_id_aleris,
	   '039630926',
	   su_duns,
	   ll_gs_log,
	   ll_gs_log,
	   SYSDATE,
	   edi_file,
	   0,
	   ll_customer_id,
	   ll_st_log,
	   '870');
      --  COMMIT;


 -- Release this commented section
 --    1. when testing the update portion of this stored procedure, or
 --    2. before releasing the SP to PROD.
 -- Patrick Reynolds May 23, 2016

     FOR m IN 0 .. k - 1 LOOP
       UPDATE production_sheet_item
	   SET prod_item_edi870_date = SYSDATE,
	       edi_file_id_870 = ll_edi_file_id_aleris /* Alex Gerlants. 09/06/2016. New column */
	WHERE production_sheet_item.prod_item_num = item_num(m);
     END LOOP;

--	COMMIT;

       FOR m in 0..p - 1
	   LOOP
	       UPDATE ab_job set ab_job.scrap_870_date = sysdate
	       WHERE ab_job.ab_job_num = job_num(m);
	    END LOOP;

      commit;

    END IF; --end if in select count(*)

  END LOOP; ------ End of outer loop customer_id

EXCEPTION
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
END;
/
