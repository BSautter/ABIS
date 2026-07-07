  CREATE OR REPLACE PROCEDURE "DBO"."P_CHECK_997" is

Cursor cur IS
SELECT *
FROM 		outbound_edi_transaction;

type line_tabletype is table of varchar2(32767) index by binary_integer;
line line_tabletype;
j binary_integer := 0;
i int;
li_edi_file_id int;
li_icn int;
ld_transaction_time date;
edi_location varchar2(50):='/templar/templar/incoming/senddata';
edi_file varchar2 (50) := 'check_997.log';
edi_file_handle UTL_FILE.FILE_TYPE;

begin

for cur_rec in cur loop
       if cur_rec.fa_received_time is null  then
	   if (sysdate - cur_rec.transaction_time > 0.083333 and --2 hours
	      sysdate - cur_rec.transaction_time < 1) then

	      line(j):= 'EDI_FILE_ID: '||cur_rec.edi_file_id||', '||'ICN: '||cur_rec.interchange_control_number||
			', '||'TRANSCATION_TIME: '||to_char(cur_rec.transaction_time, 'MM/DD/YY HH24:MI')||', '||
			'TRANSACTION_TYPE_ID: '|| cur_rec.transaction_type_id;
	      j := j + 1;
	   end if;
       end if;
end loop;

if j > 0 then
  edi_file_handle :=utl_file.fopen(edi_location,edi_file,'W');

  FOR i IN 0..j-1 LOOP
      utl_file.put_line(edi_file_handle,line(i));
    END LOOP;

  utl_file.fclose(edi_file_handle);
end if;

end P_CHECK_997;
/
