$PBExportHeader$w_da_sheet.srw
forward
global type w_da_sheet from w_sheet
end type
type uo_mtlb4feed from u_line_status_button within w_da_sheet
end type
type hpb_coil from u_hpb within w_da_sheet
end type
type cb_exit from u_cb within w_da_sheet
end type
type ole_1 from olecustomcontrol within w_da_sheet
end type
type st_4 from statictext within w_da_sheet
end type
type st_time from statictext within w_da_sheet
end type
type st_msg from statictext within w_da_sheet
end type
type cb_operatioin from commandbutton within w_da_sheet
end type
type tab_queue from tab within w_da_sheet
end type
type tabpage_coil_queue from userobject within tab_queue
end type
type dw_coil_queue from u_dw within tabpage_coil_queue
end type
type tabpage_coil_queue from userobject within tab_queue
dw_coil_queue dw_coil_queue
end type
type tab_queue from tab within w_da_sheet
tabpage_coil_queue tabpage_coil_queue
end type
type cb_change_coil from commandbutton within w_da_sheet
end type
type cb_1 from commandbutton within w_da_sheet
end type
type st_efficiency from statictext within w_da_sheet
end type
type st_3 from statictext within w_da_sheet
end type
type rb_reject from radiobutton within w_da_sheet
end type
type rb_feed from radiobutton within w_da_sheet
end type
type pb_rejectlength from u_pb within w_da_sheet
end type
type pb_feedlength from u_pb within w_da_sheet
end type
type em_reject_length from u_em within w_da_sheet
end type
type em_feed_length from u_em within w_da_sheet
end type
type uo_coilinfeeder from u_line_status_button within w_da_sheet
end type
type uo_noauto from u_line_status_button within w_da_sheet
end type
type uo_autorunning from u_line_status_button within w_da_sheet
end type
type uo_automode from u_line_status_button within w_da_sheet
end type
type st_goodpartcnt from statictext within w_da_sheet
end type
type st_scrapcnt from statictext within w_da_sheet
end type
type st_partno from statictext within w_da_sheet
end type
type st_stroke from statictext within w_da_sheet
end type
type st_10 from statictext within w_da_sheet
end type
type st_9 from statictext within w_da_sheet
end type
type st_8 from statictext within w_da_sheet
end type
type st_7 from statictext within w_da_sheet
end type
type st_6 from statictext within w_da_sheet
end type
type st_5 from statictext within w_da_sheet
end type
type st_1 from statictext within w_da_sheet
end type
type tab_da from u_da_tab within w_da_sheet
end type
type tab_da from u_da_tab within w_da_sheet
end type
type gb_1 from groupbox within w_da_sheet
end type
type st_2 from statictext within w_da_sheet
end type
type st_shift from statictext within w_da_sheet
end type
type uo_sql from u_fault_status_button within w_da_sheet
end type
type uo_opc from u_fault_status_button within w_da_sheet
end type
type uo_plc from u_fault_status_button within w_da_sheet
end type
type st_job from statictext within w_da_sheet
end type
type st_coil from statictext within w_da_sheet
end type
type dw_shift_coil from u_dw within w_da_sheet
end type
type cb_end_coil from commandbutton within w_da_sheet
end type
type st_yield from statictext within w_da_sheet
end type
type st_coil_finish_percent from statictext within w_da_sheet
end type
end forward

global type w_da_sheet from w_sheet
string tag = "0"
integer width = 3785
integer height = 2429
boolean controlmenu = false
boolean minbox = false
boolean maxbox = false
boolean resizable = false
windowstate windowstate = maximized!
long backcolor = 15780518
string icon = "jaguar.ico"
string pointer = "HyperLink!"
boolean toolbarvisible = false
event ue_reset_db ( )
event ue_close_cause_tabpg ( )
event ue_downtime_processing ( )
event ue_noauto_set ( )
event ue_noauto_clear ( )
event ue_coil_online ( )
event ue_coil_offline ( )
event ue_coil_changed ( )
event ue_job_changed ( )
event ue_override_changed ( )
event ue_end_shift ( )
event ue_refresh_display ( )
event ue_coil_unloaded ( )
event ue_restore_pre_pcs ( )
event ue_open_operation_penal ( )
uo_mtlb4feed uo_mtlb4feed
hpb_coil hpb_coil
cb_exit cb_exit
ole_1 ole_1
st_4 st_4
st_time st_time
st_msg st_msg
cb_operatioin cb_operatioin
tab_queue tab_queue
cb_change_coil cb_change_coil
cb_1 cb_1
st_efficiency st_efficiency
st_3 st_3
rb_reject rb_reject
rb_feed rb_feed
pb_rejectlength pb_rejectlength
pb_feedlength pb_feedlength
em_reject_length em_reject_length
em_feed_length em_feed_length
uo_coilinfeeder uo_coilinfeeder
uo_noauto uo_noauto
uo_autorunning uo_autorunning
uo_automode uo_automode
st_goodpartcnt st_goodpartcnt
st_scrapcnt st_scrapcnt
st_partno st_partno
st_stroke st_stroke
st_10 st_10
st_9 st_9
st_8 st_8
st_7 st_7
st_6 st_6
st_5 st_5
st_1 st_1
tab_da tab_da
gb_1 gb_1
st_2 st_2
st_shift st_shift
uo_sql uo_sql
uo_opc uo_opc
uo_plc uo_plc
st_job st_job
st_coil st_coil
dw_shift_coil dw_shift_coil
cb_end_coil cb_end_coil
st_yield st_yield
st_coil_finish_percent st_coil_finish_percent
end type
global w_da_sheet w_da_sheet

type variables
//n_tmg itmg_sheet_timer
n_cst_datetime inv_datetime
//temp for testing
//uo_one_day_timer iuo_t
//uo_seven_days_timer iuo_db
//end of temp
//uo_one_hour_timer iuo_schedule
string is_user_name
string is_remotehost
string is_coil = "abcoil" 
string is_job = "abjob"
string is_override = "abshiftoverride" 
string is_shift = "abshift" 
string is_noauto = "noauto-t"
string is_automode = "automode"
string is_autorunning = "autorunning"
string is_coilinfeeder = "coilinfeeder"
string is_mtlb4feed = "mtlb4feed"
string is_coilunload = "coilunload"
string is_coilunloadreset = "coilunloadreset"
string is_feedlength = "feedlength"
string is_goodpartcnt = "goodpartcnt"
string is_rejectpartcnt = "rejectpartcnt"
string is_rejectlength = "rejectlength"
string is_partno = "partno"
string is_strokes = "strokecnt"
string is_maintenance_password = "5678"
string is_prod_control_password = "2345"
string is_shift_super_password = "1234"
string is_scale_com_port = "5"
string is_skid_scale_online = "1"
string is_scrap_scale_online = "1"
string is_scanner_online = "1"
string is_skid = "abskid"
string is_feedreject = "feedreject"
string is_plc_fault = "activefault"
string is_opc_error = "_Attributes._ErrorCode"
//-------Stacker Items
string is_station2_stack_counter="stacker110.station2_stack_counter"
long il_station2_stack_counter
string is_station1_stack_counter="stacker110.station1_stack_counter"
long il_station1_stack_counter
string is_LastScrapRemoved="stacker110.LastScrapRemoved"
string is_LineRunoutComplete="stacker110.LineRunoutComplete"
string is_Sta2StackComplete="stacker110.Sta2StackComplete"
boolean ib_Sta2StackComplete
string is_Sta1StackComplete="stacker110.Sta1StackComplete"
boolean ib_Sta1StackComplete
string is_SendBlankToSta2="stacker110.SendBlankToSta2"
boolean ib_SendBlankToSta2
string is_SendBlankToSta1="stacker110.SendBlankToSta1"
boolean ib_SendBlankToSta1
string is_SlaveSta2ToSta1="stacker110.SlaveSta2ToSta1"
boolean ib_SlaveSta2ToSta1
string is_StackLeavingConveyor1="stacker110.StackLeavingConveyor1"
boolean ib_StackLeavingConveyor1
string is_StackLeavingSta2LiftTblConveyor="stacker110.StackLeavingSta2LiftTblConveyor"
boolean ib_StackLeavingSta2LiftTblConveyor
string is_StackLeavingSta1LiftTblConveyor="stacker110.StackLeavingSta1LiftTblConveyor"
boolean ib_StackLeavingSta1LiftTblConveyor


//-------End of Stacker items
int ii_com_port
int ii_event_id
int ii_manual_stack_lenght

long il_abcoil, il_abjob,/*current job*/ il_job /*just for display*/, il_override, il_abshift, il_goodpartcnt
long il_rejectpartcnt, il_partno, il_strokes
boolean ib_noauto, ib_automode, ib_autorunning, ib_coilinfeeder, ib_coilunload, ib_good_part_as_reject,ib_shift_ending, ib_coil_ending
Real ir_feedlength, ir_reject_length
uo_seven_days_timer iuo_db
uo_timing_get_shift iuo_timing_get_shift
int ii_line_id
int ii_plc_fault
long il_opc_error, il_message_long
//w_change_job iw_change_job
w_super_validation iw_super_validation
w_dt_change_job_reminder iw_dt_change_job_reminder
w_override iw_override
//w_maintenance_validation iw_maintenance_validation
w_please_wait iw_please_wait
//w_please_wait_end_shift iw_please_wait_end_shift
//w_maintenance iw_maintenance
w_operation_panel iw_operation_panel
//w_scan_coil_id iw_scan_coil_id
uo_shift iuo_shift
u_dt_cause_tabpg iu_cause_tabpg
//u_causes iu_causes
//u_causes iu_causes
u_coil iu_current_coil
//w_dt_one_min_log iw_dt_log
n_tmg itmg_refresh_display
long il_pre_length_pcs, il_pre_reject_pcs
boolean ib_pre_coil_continue = false
boolean ib_post_open = false
boolean ib_feedreject = true
boolean ib_offline_mode = false
boolean ib_skid_scale_connected
boolean ib_scrap_scale_connected
boolean ib_scanner_connected
boolean ib_stacker_online = false
boolean ib_mtlb4feed = false
CONSTANT integer operation_panel = 0
CONSTANT integer newcoil = 1
CONSTANT integer endcoil = 2
CONSTANT integer endshift =3
CONSTANT integer changejob =4
DateTime idt_endshift_summary_print_shift_end_time
Long il_endshift_summary_shift_id
//CONSTANT integer newcoil = 1
window iw_production_folder_shop
int ii_feedlenght


end variables

forward prototypes
public subroutine enable_press (boolean ab_1)
public function integer wf_new_shift (long al_shift_id)
public function integer wf_end_shift ()
public subroutine wf_set_opc_abjob (long al_job)
public subroutine wf_set_opc_abcoil (long al_coil)
public subroutine wf_show_dw_process_status ()
public subroutine wf_show_job_sheet ()
public function string wf_get_partial_wh (long al_job)
public subroutine wf_show_report ()
public subroutine wf_show_dt_report_summary ()
public subroutine wf_show_shift_coil ()
public function long wf_read_scale ()
public function integer wf_zero_scale ()
public subroutine wf_print_shift_summary (long al_shift_id)
public subroutine wf_change_job_same_coil ()
public subroutine wf_shift_override ()
public subroutine wf_set_operation_id (integer ai_id)
public subroutine wf_open_operation_panel ()
public function boolean wf_is_db_alive ()
public function long wf_get_shift_id (datetime adt_now)
public function integer wf_done_coil ()
public function integer wf_super_validation ()
public subroutine wf_set_ejob_window (readonly window aw_window)
public subroutine wf_set_opc_abskid (long al_skid, integer ai_stacker)
end prototypes

event ue_reset_db();sqlca.of_disconnect( )
IF SQLca.of_connect( ) = -1 then
	//gnv_app.inv_error.of_message( "Database", "Unable to connect ", StopSign!, OK!, 1, 5, true, true)
	//MessageBox("Database", "Unable to connect " + string(today()) + " " + string(now()) )
	HALT close
ELSE
	string ls_setrole
	ls_setrole = "set role update_role identified by __DB_PASSWORD_REDACTED__"
	sqlca.of_execute( ls_setrole)
	//EXECUTE IMMEDIATE :ls_setrole USING SQLCA;
	IF sqlca.sqlcode <> 0 then
		//gnv_app.inv_error.of_message( "Database", "Unable to set role ", StopSign!, OK!, 1, 5, true, true)
		this.setmicrohelp( "Connection failed to set role " + string(today()) + " " + string(now()))
	else	
		this.setmicrohelp( "Connection complete " + string(today()) + " " + string(now()))
	end if
	dw_shift_coil.of_settransobject( sqlca)
	tab_queue.tabpage_coil_queue.dw_coil_queue.of_settransobject( sqlca)
end if

end event

event ue_close_cause_tabpg();//MessageBox(" ", "ue_close_cause_tabpg")
tab_da.closetab( iu_cause_tabpg )
end event

event ue_downtime_processing();if message.wordparm = 1 then
	//enable_press( true) //downtime processing start
else
	//enable_press( false ) //downtime processing done
end if
end event

event ue_noauto_set();//
end event

event ue_noauto_clear();//
end event

event ue_coil_online();if isValid(iuo_shift) then
	//MessageBox("ue_coil_online", "triggered")
	if iuo_shift.of_is_init_done( ) then
			//iuo_shift.postevent( "ue_coil_online")
			iuo_shift.event ue_coil_online( )
	end if
end if
//if il_abcoil > 10000 then
//	
//end if
end event

event ue_coil_offline();if ib_coil_ending then
	return
end if
ib_coil_ending  = true
if isValid(iuo_shift) then
	if iuo_shift.of_is_init_done( ) then
		iuo_shift.postevent( "ue_coil_offline" )
	end if
else
	if isValid( iu_current_coil ) then
		OpenWithParm( w_done_coil, iu_current_coil, this  )
		if not isValid(iu_current_coil) then
			ib_coil_ending = false
			return
		end if
		if iu_current_coil.get_new_status( ) <> 1 then	
			iu_current_coil.save( )
			wf_show_shift_coil( )
			wf_show_dw_process_status( )
			wf_set_opc_abcoil( -1 )
			destroy iu_current_coil
		end if
		
	end if
end if
ib_coil_ending = false
end event

event ue_coil_changed();//return
long ll_value
ll_value = message.longparm
MessageBox("ue_coil_changed", string( ll_value ) )

if ll_value = 0 then
	if not isValid( iu_current_coil ) then
		ole_1.object.items(is_coil ).value = -1
		ole_1.object.update
	else
		ole_1.object.items(is_coil ).value = iu_current_coil.get_coil_abc_id( )
		ole_1.object.update
	end if
else
	il_abcoil = ll_value
	if isValid(iu_current_coil) then
		st_coil.text = iu_current_coil.get_coil_org_id( )
	else
		st_coil.text = "None"
	end if
end if

end event

event ue_job_changed();long ll_value
ll_value = message.longparm
MessageBox("ue_job_changed", string( ll_value ))
if ll_value = 0 then
	if il_abjob < 1000 then
		ole_1.object.items(is_job ).value = -1
		ole_1.object.update
	else
		ole_1.object.items(is_job ).value = il_abjob
		ole_1.object.update
	end if
else
	il_abjob = ll_value
	if ll_value = -1 then
		st_job.text = "None"
	else
		st_job.text = string( ll_value )
	end if
end if

end event

event ue_end_shift();wf_end_shift( )
end event

event ue_refresh_display();st_time.text = string(now())
if isValid( iuo_shift ) then
	long ll_shift_run_time
	ll_shift_run_time = secondsafterabs( Time(iuo_shift.idt_shift_start), now())
	if ll_shift_run_time <> 0 then
		st_efficiency.text =  String(Int(((ll_shift_run_time - iuo_shift.il_totol_downtime_sec) / ll_shift_run_time) * 100)) + "%"
	end if
	
	if wf_is_db_alive( ) then
		uo_sql.set_select( true)
	else
		uo_sql.set_select( false)
		this.event ue_reset_db( )
	end if
	
	if isValid( iu_current_coil ) then
//		st_coil_finish_percent.text = string( iu_current_coil.of_get_finish_percent( ) ) + "%"
		double ld_yield
		
		if ib_pre_coil_continue then
			this.event ue_restore_pre_pcs( )
			ib_pre_coil_continue = false
		end if
		
		ld_yield = iu_current_coil.of_get_yield( )
		if ld_yield > 95 then
			st_yield.textcolor = RGB(0,0,0)
		else
			st_yield.textcolor = RGB(255,0,0)
		end if
		
		st_yield.text = string( ld_yield ) + "yd%"
		hpb_coil.position = iu_current_coil.of_get_finish_percent( )
		st_coil.text = iu_current_coil.get_coil_org_id( )
		
//		if iu_current_coil.of_get_finish_percent( ) > 15 then
//			
//		end if
		
//		if iu_current_coil.of_get_finish_percent( ) < 90 then
//			cb_end_coil.enabled = false
//			cb_end_coil.visible = false
//		else
//			cb_end_coil.enabled = true
//			cb_end_coil.visible = true
//		end if
		
		
		
		
		if iu_current_coil.of_get_finish_percent( ) > 90 or  (((not ib_coilinfeeder) or (not ib_mtlb4feed) ) and iu_current_coil.get_nw_bal( ) < iu_current_coil.get_old_nw( )) then //and ( Long(iu_current_coil.of_get_scrap_tab_page( ).dw_current_quality_scrap.object.scrap_item_piece[1] ) > 0 or &
//			iu_current_coil.of_get_skid_tab_page( ).dw_current_edit.enabled or iu_current_coil.get_nw_bal( ) < iu_current_coil.get_old_nw( ) ) then
			ii_event_id = endcoil
			if not cb_end_coil.enabled then cb_end_coil.enabled = true
			if not cb_end_coil.visible then cb_end_coil.visible = true
//		else
//			cb_end_coil.enabled = false
//			cb_end_coil.visible = false
		end if
	else //No coil
//		st_coil_finish_percent.text = "None"
		hpb_coil.position = 0
		st_coil.text = "None"
		st_yield.text = "0" + "yd%"
//		if ib_coilinfeeder  and iuo_shift.il_coil_id < 1000 and iuo_shift.of_is_init_done( ) then
//			ii_event_id = 1
//			wf_open_operation_panel( )
//		end if

		if cb_end_coil.enabled then cb_end_coil.enabled = false
		if cb_end_coil.visible then cb_end_coil.visible = false
		
		if ib_coilinfeeder  and iuo_shift.of_is_init_done( ) and (not isValid( iw_super_validation )) then
			ii_event_id = newcoil
			wf_open_operation_panel( )
		end if

	end if

	//Check shift end time
	
	if DateTime(Today(), Now()) >  inv_datetime.of_relativedatetime( iuo_shift.get_schedule_end_time( ), 1200)  then
		wf_end_shift( )
	end if
	
	
else
	st_efficiency.text = "0%"
	st_coil_finish_percent.text = "0%"
end if



end event

event ue_coil_unloaded();//if isValid( iu_current_coil ) then
//	OpenwithParm( w_finish_coil , iu_current_coil )
//	if Not isValid(iu_current_coil) then
//		return
//	end if
//	iu_current_coil.save( )
//	
////	if isValid( iuo_shift ) and isValid(iuo_shift.iu_current_coil) then
////		iuo_shift.iu_current_coil.save( )
////		destroy iuo_shift.iu_current_coil
////	end if
//	wf_show_dw_process_status( )
//	wf_show_shift_coil( )
//	ole_1.object.items( is_coil ).value = -1
	ole_1.object.items(is_coilunloadreset).value = true
	ole_1.object.update
	ole_1.object.items(is_coilunloadreset).value = false
	ole_1.object.update
//	destroy iu_current_coil
//end if
end event

event ue_restore_pre_pcs();//
ole_1.object.items(is_goodpartcnt ).value = il_pre_length_pcs
ole_1.object.items(is_rejectpartcnt ).value = il_pre_reject_pcs
ole_1.object.update
end event

event ue_open_operation_penal();wf_open_operation_panel( )
end event

public subroutine enable_press (boolean ab_1);boolean lb_1
lb_1 = not ab_1
if ib_noauto <> lb_1 then
	ole_1.object.items(is_noauto ).value = lb_1
	ole_1.object.update
end if
end subroutine

public function integer wf_new_shift (long al_shift_id);int li_rc
if isValid(iuo_shift) then
	wf_end_shift( )
end if

iuo_shift = create uo_shift

  SELECT "LINE_CURRENT_STATUS"."AB_JOB_NUM"  
    INTO :il_abjob  
    FROM "LINE_CURRENT_STATUS"  
   WHERE ( "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id ) AND  
         ( "LINE_CURRENT_STATUS"."SHIFT_NUM" = :al_shift_id )   
           ;
if isNull(il_abjob ) or il_abjob < 1000 then
	il_abjob = ole_1.object.items( is_job ).value
end if
li_rc = iuo_shift.init( al_shift_id , ii_line_id, il_abjob ,this )
if li_rc <> 0 then
	return li_rc
end if
	
//	if not isValid( iu_current_coil ) then
//		long ll_pre_job, ll_pre_coil, ll_pre_shift
//		
//		
//		
//	end if
	
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHIFT_NUM" = :al_shift_id  
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id   
           ;
		if sqlca.sqlcode <> 0 then
			sqlca.of_rollback( )
			return 1
		end if
	sqlca.of_commit( )
	ole_1.object.items( is_shift ).value = al_shift_id
	ole_1.object.update
	
	tab_queue.tabpage_coil_queue.dw_coil_queue.of_retrieve( )
//	tab_queue.tabpage_job_queue.dw_job_queue.retrieve( ii_line_id )
	tab_queue.selecttab( 1 )
	il_abshift = al_shift_id
	wf_show_shift_coil( )
	if not ib_offline_mode then
		ole_1.object.items( is_strokes ).value = 0
		ole_1.object.update
	end if
	return li_rc

end function

public function integer wf_end_shift ();if ib_shift_ending then
	return 1
end if
ib_shift_ending = true
String ls_msg = "Shift ending!"
if not isValid( iw_please_wait ) then 
	OpenWithParm( iw_please_wait, ls_msg, this )
end if
if isValid(iuo_shift) then
	long ll_shift_id
	ll_shift_id = iuo_shift.get_shift_id( )
	
	st_shift.text = "Shift ended at " + string(now())
	il_abshift = -1
	if isValid( iu_current_coil ) then
		iu_current_coil.of_save_at_shift_end( )
	end if
	   
		DateTime ldt_dt
		ldt_dt = DateTime(today(), Now())
		long ll_shift_downtime	
		SELECT SUM(DT_INSTANCE.ENDING_TIME - DT_INSTANCE.STARTING_TIME) * 86400 INTO :ll_shift_downtime
			FROM DT_INSTANCE
		WHERE DT_INSTANCE.SHIFT_NUM = :ll_shift_id ;
		
//		UPDATE "LINE_CURRENT_STATUS"  
//     	SET "SHIFT_NUM" = NULL  
//   	WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id   
//           ;
//		if sqlca.sqlcode <> 0 then
//			sqlca.of_rollback( )
//			if isValid(iw_please_wait_end_shift) then
//				close(iw_please_wait_end_shift)
//			end if
//			MessageBox("Shift end", "End shift failed. Update line_current_status failed!")
//			ib_shift_ending = false
//			return 1
//		end if
		
		
		UPDATE "SHIFT"  
     	SET "END_TIME" = :ldt_dt, "DT_TOTAL" = :ll_shift_downtime  
   	WHERE "SHIFT"."SHIFT_NUM" = :ll_shift_id ;
		
		if sqlca.sqlcode <> 0 then
			sqlca.of_rollback( )
			MessageBox("Shift end", "End shift failed!")
			if isValid(iw_please_wait) then
				close(iw_please_wait)
			end if
//			MessageBox("End shift", "Update shift failed!")
			ib_shift_ending = false
			return 1
//		else
			
		end if
	
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHIFT_NUM" = null  
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id   
           ;
		if sqlca.sqlcode <> 0 then
			sqlca.of_rollback( )
			MessageBox("Shift end", "End shift failed!")
			if isValid(iw_please_wait) then
				close(iw_please_wait)
			end if
			ib_shift_ending = false
			return 1
		end if  
	
	
	
	sqlca.of_commit( )
	
	ole_1.object.items( is_shift ).value = -1
	ole_1.object.update
	
	destroy iuo_shift
	//---------Print shift changed to shift end button
	//---------------------------------------
	//wf_print_shift_summary( ll_shift_id )
	//----------------------------------------
	
	il_endshift_summary_shift_id = ll_shift_id
	idt_endshift_summary_print_shift_end_time = ldt_dt
	
end if


if isValid(iw_please_wait) then
	close(iw_please_wait)
end if

ib_shift_ending = false

if isValid(iw_please_wait) then
	close(iw_please_wait)
end if
if isValid(iw_please_wait) then
	close(iw_please_wait)
end if
return 0
end function

public subroutine wf_set_opc_abjob (long al_job);if al_job > 1000 then
	ole_1.object.items(is_job).value = al_job
	ole_1.object.update

	UPDATE "LINE_CURRENT_STATUS"  
     SET "AB_JOB_NUM" = :al_job  
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		return
	end if
		
	//Add update line_priority table current job as running job 	
	
//	UPDATE "LINE_PRIORITY"  
//   	SET "STATUS" = 0  
//   WHERE "LINE_PRIORITY"."STATUS" = 1 and
//			"LINE_PRIORITY"."LINE_NUM" = :ii_line_id;
	
	
	UPDATE "LINE_PRIORITY"  
   	SET "STATUS" = 2  
   WHERE "LINE_PRIORITY"."LINE_NUM" = :ii_line_id and
			"LINE_PRIORITY"."STATUS" = 1
           ;
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		return
	end if	
		
	UPDATE "LINE_PRIORITY"  
   	SET "STATUS" = 1  
   WHERE "LINE_PRIORITY"."AB_JOB_NUM" = :al_job and
			"LINE_PRIORITY"."LINE_NUM" = :ii_line_id
           ;
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		return
	end if
		sqlca.of_commit( )
		if isValid( iw_production_folder_shop ) then iw_production_folder_shop.postevent("ue_timely_refresh")
else
	ole_1.object.items(is_job).value = -1
	ole_1.object.update

	UPDATE "LINE_CURRENT_STATUS"  
     SET "AB_JOB_NUM" = NULL  
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		MessageBox("Job update", "Failed to update current job status")
		return
	else
		sqlca.of_commit( )
	end if
end if
end subroutine

public subroutine wf_set_opc_abcoil (long al_coil);if al_coil > 1000 then
	ole_1.object.items(is_coil).value = al_coil
	ole_1.object.update
  	UPDATE "LINE_CURRENT_STATUS"  
     	SET "COIL_ABC_NUM" = :al_coil, "COIL_PROCESS_RATE" = 0 
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
   ;
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		return

	end if

	  UPDATE "COIL"  
     SET "COIL_STATUS_FROM_LINE" = 1  
   WHERE "COIL"."COIL_ABC_NUM" = :al_coil  
           ;
		if sqlca.sqlcode <> 0 then
			sqlca.of_rollback( )
			return
		end if
		
		sqlca.of_commit( )

else
	ole_1.object.items(is_coil).value = -1
	ole_1.object.update
  	UPDATE "LINE_CURRENT_STATUS"  
     	SET "COIL_ABC_NUM" = NULL, "COIL_PROCESS_RATE" = NULL
		  WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
   ;
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		messagebox( "Update coil", "Fail to update current coil!") //, /*icon ae_icon*/, /*button ae_button*/, /*integer ai_default */)
		return
	else
		sqlca.of_commit( )
	end if
end if
end subroutine

public subroutine wf_show_dw_process_status ();if il_abjob < 1000 or (not isValid(iuo_shift)) then
	return
end if
w_please_wait lw_wait
string ls_msg = "Retrieving data!"
openWithParm(lw_wait, ls_msg, this)
//	tab_da.tabpage_shift_coil.dw_process_coil.settransobject( sqlca )
//	tab_da.tabpage_shift_coil.dw_process_coil.retrieve( il_abjob )
close(lw_wait)

end subroutine

public subroutine wf_show_job_sheet ();int li_sheettype, li_pic_id
long ll_job_num
string ls_customer
w_please_wait lw_wait
Blob lb_pic
String ls_cust, ls_shape, ls_tcust
Long ll_order, ll_cust
Real lr_l, lr_s, lr_w, lr_l_p, lr_l_m, lr_s_p, lr_s_m, lr_w_p, lr_w_m
Int li_item
Long ll_coil_wt, ll_row, ll_i, ll_skids, ll_max_skid_wt
Real lr_yield, lr_pcwt
String ls_modstring
Int li_skid_num

if il_job = il_abjob or il_abjob = 0 or isNull(il_abjob) then
	return
end if
string ls_msg = "Retrieving job data!"
openWithparm(lw_wait, ls_msg)

//il_job = Long(Mid(this.text(index), 1, 5))

ll_job_num = il_abjob


SELECT CUSTOMER_ORDER.SHEET_HANDLING_TYPE INTO :li_sheettype
FROM AB_JOB, CUSTOMER_ORDER 
WHERE (AB_JOB.ORDER_ABC_NUM = CUSTOMER_ORDER.ORDER_ABC_NUM) AND (AB_JOB.AB_JOB_NUM = :ll_job_num)   
USING SQLCA;


SELECT sketch_id //, sketch_job_note
INTO :li_pic_id //,:mle_desc.Text 
FROM ab_job
WHERE ab_job_num = :ll_job_num
USING SQLCA;
IF SQLCA.SQLCode = -1 THEN
	Return
END IF

SELECTBLOB sketch_view 
INTO :lb_pic
FROM sketch
WHERE sketch_id = :li_pic_id
USING SQLCA;
IF SQLCA.SQLCode = -1 THEN
	Return
END IF

int li_FileNum, li_loops, li_i
Long ll_flen, ll_new_pos
ll_flen = Len(lb_pic)
IF ll_flen > 32765 THEN
	IF Mod(ll_flen, 32765) = 0 THEN
		li_loops = ll_flen / 32765
	ELSE
		li_loops = (ll_flen / 32765) + 1
	END IF
ELSE
	li_loops = 1
END IF

ll_new_pos = 1
li_filenum = FileOpen(gs_Sketch_bmp_file, StreamMode!, Write!, LockReadWrite!, Replace!)
FileWrite(li_filenum, BlobMid(lb_pic, 0, 32765))
FileClose(li_FileNum)

FOR li_i = 1 TO li_loops
	li_filenum = FileOpen(gs_Sketch_bmp_file, StreamMode!, Write!, LockreadWrite!,Append!)
	FileWrite(li_filenum, BlobMid(lb_pic, li_i*32765, 32765) )
	FileClose(li_FileNum)
NEXT

//p_1.SetPicture(lb_pic)
//p_1.show( )
//p_1.

CHOOSE CASE li_sheettype
	CASE 1
		//new interface
		//tab_da.tabpage_job.dw_job_sheet.DataObject = "d_report_prod_order_by_lot"
	CASE ELSE
		//new interface
		//tab_da.tabpage_job.dw_job_sheet.DataObject = "d_report_prod_order"
END CHOOSE





//dw_1.object.p_1.SetPicture(lb_pic)
//dw_1.object.p_1.show()
//p_1.SetPicture(lb_pic)

//p_1.show( )
//CONNECT USING SQLCA;

SELECT order_abc_num, order_item_num, material_yield 
INTO :ll_order, :li_item, :lr_yield
FROM ab_job 
WHERE ab_job_num = :ll_job_num
USING SQLCA;

SELECT orig_customer_id INTO :ll_cust
FROM customer_order
WHERE order_abc_num = :ll_order
USING SQLCA;
IF ll_cust > 0 THEN
	SELECT customer_short_name INTO :ls_tcust
	FROM customer
	WHERE customer_id = :ll_cust
	USING SQLCA;
	ls_cust =  Upper(Trim(ls_tcust))
	ls_modstring = "cust_t.Text = ~"" + ls_cust + "~""
	//new interface
	//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring) 
END IF

SELECT sheet_type, max_skid_wt, theoretical_unit_wt INTO :ls_shape, :ll_max_skid_wt, :lr_pcwt
FROM order_item
WHERE order_abc_num = :ll_order AND order_item_num = :li_item
USING SQLCA;

//part dimensions should be Width x Length 
CHOOSE CASE Upper(Trim(ls_shape))
	CASE "RECTANGLE"
		SELECT rt_length, rt_length_plus, rt_length_minus, rt_width,  rt_width_plus, rt_width_minus
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM rectangle
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "PARALLELOGRAM"
		SELECT p_length, p_length_plus,p_length_minus, p_width,  p_width_plus,  p_width_minus  
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM parallelogram
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "FENDER"
		SELECT fe_side, fe_side_plus, fe_side_minus INTO :lr_w, :lr_w_p, :lr_w_m
		FROM fender
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "CHEVRON"
		SELECT ch_length, ch_length_plus, ch_length_minus, ch_width, ch_width_plus, ch_width_minus
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM chevron
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "CIRCLE"
		SELECT c_diameter, c_diameter_plus, c_diameter_minus INTO :lr_w, :lr_w_p, :lr_w_m
		FROM circle
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "TRAPEZOID"
		SELECT tr_long_length, tr_long_plus, tr_long_minus, tr_width, tr_width_plus , tr_width_minus  
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM trapezoid
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "L.TRAPEZOID"
		SELECT ltr_long_length, ltr_long_plus, ltr_long_minus, ltr_width, ltr_width_plus, ltr_width_minus  
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM left_trapezoid
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "R.TRAPEZOID"
		SELECT rtr_long_length, rtr_long_plus, rtr_long_minus, rtr_width, rtr_width_plus, rtr_width_minus  
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM right_trapezoid
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "REINFORCEMENT"
		SELECT re_length, re_length_plus, re_length_minus, re_width,  re_width_plus, re_width_minus
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM reinforcement
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "LIFTGATE"
		SELECT li_length, li_length_plus, li_length_minus, li_width,  li_width_plus, li_width_minus
		INTO :lr_l, :lr_l_p, :lr_l_m, :lr_w, :lr_w_p, :lr_w_m
		FROM liftgate_shape
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
	CASE "OTHER"
		SELECT x_1, x_2 
		INTO :lr_w, :lr_l
		FROM x1_shape
		WHERE order_abc_num = :ll_order AND order_item_num = :li_item
		USING SQLCA;
END CHOOSE		

ls_modstring = "w_t.Text = ~"" + String(lr_w, "###,###.###") + "~""
//new interface
//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring)
IF IsNULL(lr_w_p) OR lr_w_p = 0 THEN
	ls_modstring = "w_p_t.Text = ~"+ 0.000~""
ELSE	
	ls_modstring = "w_p_t.Text = ~"+" + String(lr_w_p, "###.###") + "~""
END IF
//new interface
//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring) 
IF IsNULL(lr_w_m) OR lr_w_m = 0 THEN 
	ls_modstring = "w_m_t.Text = ~"- 0.000~""
ELSE	
	ls_modstring = "w_m_t.Text = ~"-" + String(lr_w_m, "##.###") + "~""
END IF
//new interface
//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring) 

ls_modstring = "l_t.Text = ~"" + String(lr_l, "###,###.###") + "~""
//new interface
//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring) 
IF NOT(IsNULL(lr_l) OR (lr_l = 0)) THEN
	IF IsNULL(lr_l_p) OR lr_l_p = 0 THEN
		ls_modstring = "l_p_t.Text = ~"+ 0.000~""
	ELSE		
		ls_modstring = "l_p_t.Text = ~"+" + String(lr_l_p, "##.###") + "~""
	END IF
	//new interface
	//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring)
	IF IsNULL(lr_l_m) OR lr_l_m = 0 THEN 
		ls_modstring = "l_m_t.Text = ~"- 0.000~""
	ELSE	
		ls_modstring = "l_m_t.Text = ~"-" + String(lr_l_m, "##.###") + "~""
	END IF
	//new interface
	//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring) 
END IF

DataStore lds_coil
lds_coil = CREATE datastore  
lds_coil.DataObject = "d_report_ab_job_coil"  
lds_coil.SetTransObject (SQLCA)  
lds_coil.Retrieve(ll_job_num)

ll_row = lds_coil.rowCount()
ll_coil_wt = 0
IF ll_row > 0 THEN
	FOR ll_i = 1 TO ll_row
		ll_coil_wt = ll_coil_wt + lds_coil.GetItemNumber(ll_i, "process_coil_process_quantity")
	NEXT
END IF
DESTROY lds_coil

ls_modstring = "total_coil_t.Text = ~"" + String(ll_coil_wt, "###,###,###") + "~""
//new interface
//tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring) 

 ls_modstring = "partial_t.Text = ~"" + wf_get_partial_wh(ll_job_num) + "~""
/*------------ Need changes new interface	
	tab_da.tabpage_job.dw_job_sheet.Modify(ls_modstring) 
	tab_da.tabpage_job.dw_job_sheet.SetTransObject(sqlca)
	tab_da.tabpage_job.dw_job_sheet.Retrieve(ll_job_num)
	il_job = ll_job_num
*/ //new interface
close(lw_wait)

end subroutine

public function string wf_get_partial_wh (long al_job);String ls_s
DataStore lds_p
Long ll_row, ll_i

ls_s = ""

lds_p = CREATE DataStore
lds_p.DataObject = "d_ab_job_process_partial"
lds_p.SetTransObject(SQLCA)
lds_p.Retrieve(al_job)
ll_row = lds_p.RowCount()
IF ll_row > 0 THEN 
	ls_s = "use partial skid "
	FOR ll_i = 1 TO ll_row 
		ls_s = ls_s + " " + String(lds_p.GetItemNumber(ll_i,"sheet_skid_num"))
	NEXT
END IF
DESTROY lds_p

RETURN ls_s
end function

public subroutine wf_show_report ();if not isValid(iuo_shift) then
	return
end if

w_please_wait lw_wait
string ls_msg = "Retrieving data!"
openwithparm(lw_wait, ls_msg, this)

//new interface
//tab_da.tabpage_dt_log.dw_downtime_log.SetTransObject(sqlca)
//tab_da.tabpage_dt_log.dw_downtime_log.Retrieve( iuo_shift.get_shift_id( ) )

close(lw_wait)

end subroutine

public subroutine wf_show_dt_report_summary ();if not isValid(iuo_shift) then
	return
end if
w_please_wait lw_wait
string ls_msg = "Retrieve data!"
openwithparm(lw_wait, ls_msg ,this)
//new interface
//tab_da.tabpage_dt_summary.dw_shift_summary.SetTransObject(sqlca)
//tab_da.tabpage_dt_summary.dw_shift_summary.Retrieve( iuo_shift.get_shift_id( ) )
close(lw_wait)

end subroutine

public subroutine wf_show_shift_coil ();if not isValid(iuo_shift) then
	return
end if
//w_please_wait lw_wait
long ll_shift_id
ll_shift_id = iuo_shift.get_shift_id( )
//open(lw_wait, this)
	dw_shift_coil.retrieve( ll_shift_id )
//close(lw_wait)

end subroutine

public function long wf_read_scale ();long ll_rc
//li_rc = SioKeycode(0)
String ls_put, ls_get//, ls_msg
ls_put = "HELLO"
//ls_get = "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890"
ls_get = "1234567890123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890"
//ls_msg =                                                                                                    "                                                           "

if not ib_scrap_scale_connected then
	return -1
end if

string(SioKeycode(WSC_KEY_CODE))
ll_rc = SioReset( ii_com_port,512, 512)
	if ll_rc < 0 then
		SioWinError(ls_get,128)
		MessageBox("System error", "Can not open serial port! " + ls_get)
		return -1
	end if
	SioParms(ii_com_port, WSC_NoParity, WSC_OneStopBit, WSC_WordLength8)
	SioBaud( ii_com_port,WSC_Baud9600)
SioPutc( ii_com_port, 'b') //'a' for ZERO, 'b' for Print
sleep(1) //wait for data coming back
SioGets( ii_com_port,ls_get, 128)
//MessageBox("back", ls_get)
ls_get = Trim(Mid(ls_get, 3, 9)) //Trim non numaric char
//MessageBox("back", ls_get)
if isNumber(ls_get)  then
	ll_rc = Long( ls_get )
else
	ll_rc = 0
end if

SioDone(ii_com_port)
return ll_rc

end function

public function integer wf_zero_scale ();int li_rc
//li_rc = SioKeycode(0)
String ls_get, ls_msg
//ls_put = "HELLO"
if not ib_scrap_scale_connected then
	return 0
end if


ls_get = "1"
ls_msg = "1"
string(SioKeycode(WSC_KEY_CODE))
li_rc = SioReset( ii_com_port,512, 512)
	if li_rc < 0 then
		SioWinError(ls_msg,128)
		MessageBox("System error", "Can not open serial port! " + ls_msg)
		return -1
	end if
	SioParms(ii_com_port, WSC_NoParity, WSC_OneStopBit, WSC_WordLength8)
	SioBaud( ii_com_port,WSC_Baud9600)
SioPutc( ii_com_port, 'a') //'a' for ZERO, 'b' for Print
//sleep(1) //wait for data coming back
//SioGets( ii_com_port,ls_get, 128)
////ls_get = Trim(Mid(ls_get, 3, 9)) //Trim non numaric char
////MessageBox("back", ls_get)
SioDone(ii_com_port)
return 0

end function

public subroutine wf_print_shift_summary (long al_shift_id);datastore lds_print
	lds_print = create Datastore
	//ls_efficiency	= "t_efficiency.TEXT= ~"" + "99%" + "~""
	
	lds_print.dataobject = "d_da_daily_prod_dt_total"
	lds_print.SetTransObject(sqlca)
	lds_print.retrieve(al_shift_id)
	
	//lds_print.modify( ls_efficiency )
	lds_print.print()
	lds_print.print()

end subroutine

public subroutine wf_change_job_same_coil ();//
if not isValid( iuo_shift ) then
	//MessageBox("1","")
	return
elseif (not iuo_shift.of_is_init_done( )) or (not isValid(iu_current_coil)) or ( il_abjob < 1000 )  then
	//MessageBox("2", string(not iuo_shift.of_is_init_done( ))+string(not isValid(iuo_shift.iu_current_coil))+string(il_abjob < 1000)  )
	return
else
	//MessageBox("3","")
	iuo_shift.of_open_w_change_job_same_coil( )
end if
end subroutine

public subroutine wf_shift_override ();long ll_rc
DateTime ldt_overrided_endtime
if not isValid( iuo_shift ) then
	return
elseif not iuo_shift.of_is_init_done( ) then
	return
else
	
	if wf_super_validation( ) <> 1 then
		return
	end if
	if not isValid(iuo_shift) then
		return
	end if
		Open(iw_override)
		if not isValid(iuo_shift) then
			return
		end if
		ll_rc = message.doubleparm
		if ll_rc = 0 then return
		if ll_rc < 0 then
			wf_end_shift( )
		else
			if isValid( iuo_shift ) then
				ldt_overrided_endtime = inv_datetime.of_relativedatetime( iuo_shift.get_schedule_end_time( ) , ll_rc )
				iuo_shift.of_set_schedule_end_time( ldt_overrided_endtime )
			end if
		end if
		ole_1.object.items( is_override ).value = ll_rc
		ole_1.object.update
end if
end subroutine

public subroutine wf_set_operation_id (integer ai_id);ii_event_id = ai_id
end subroutine

public subroutine wf_open_operation_panel ();if isValid( iw_super_validation ) then return
if not isValid( iw_operation_panel ) then
	OpenwithParm(iw_operation_panel, this, this  )
//else
//	choose case ii_event_id
//	case newcoil
//		iw_operation_panel.cb_new_coil.event clicked( )
//		iw_operation_panel.cb_new_coil.enabled = true
//	case endcoil
//		iw_operation_panel.cb_end_coil.event clicked( )
//		iw_operation_panel.cb_end_coil.enabled = true
//	end choose
//	
//	iw_operation_panel.show( )
	
end if

end subroutine

public function boolean wf_is_db_alive ();int ll_1
//sqlca.of_connect( )
SELECT 1 INTO :ll_1 FROM DUAL;

if sqlca.sqlcode = 0 then
	return true
else
	return false
end if

end function

public function long wf_get_shift_id (datetime adt_now);long ll_shift_id
int li_line

if isValid( iuo_shift ) then
	return -1
end if

//li_line = this.ii_line_id
  SELECT "SHIFT"."SHIFT_NUM"  
    INTO :ll_shift_id  
    FROM "SHIFT"  
   WHERE ( "SHIFT"."LINE_NUM" = :ii_line_id ) AND  
         ( "SHIFT"."PLANNED_STARTING_TIME" <= :adt_now ) AND  
         ( "SHIFT"."PLANNED_ENDING_TIME" > :adt_now ) and
			( "SHIFT"."END_TIME" IS NULL);
if isNull(ll_shift_id) or ll_shift_id < 1000 then
	return -1
else
	return ll_shift_id
end if
end function

public function integer wf_done_coil ();	if isValid( iu_current_coil )	then
		
		int li_rc
		li_rc = iu_current_coil.save( )
		wf_set_opc_abcoil( -1 )
//		For testing ............... No job change when coil done		
//		wf_set_opc_abjob( -1)
//		End of for testing.........
		
		destroy iu_current_coil
		tab_queue.tabpage_coil_queue.dw_coil_queue.of_retrieve( )
//		tab_queue.tabpage_job_queue.dw_job_queue.retrieve( ii_line_id )
		wf_show_shift_coil( )
		return li_rc
	end if
	return 1
end function

public function integer wf_super_validation ();Int li_rc
if not isValid(iw_super_validation ) then
		OpenWithparm(iw_super_validation, this, this )
		li_rc = message.doubleparm
		return li_rc
end if
return 0
end function

public subroutine wf_set_ejob_window (readonly window aw_window);iw_production_folder_shop = aw_window
end subroutine

public subroutine wf_set_opc_abskid (long al_skid, integer ai_stacker);if al_skid > 1000 then
	ole_1.object.items(is_skid).value = al_skid
	ole_1.object.update
	

choose case ai_stacker
	case 1
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHEET_SKID_STACKER_1" = :al_skid 
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
	case 2
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHEET_SKID_STACKER_2" = :al_skid 
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
	case else
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHEET_SKID_NUM" = :al_skid 
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
end choose


//	
//	UPDATE "LINE_CURRENT_STATUS"  
//     SET "SHEET_SKID_NUM" = :al_skid 
//   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
// 	;
//	
	
	
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		return
	else
		sqlca.of_commit( )
	end if
else
	ole_1.object.items(is_skid).value = -1
	ole_1.object.update
	
	choose case ai_stacker
	case 1
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHEET_SKID_STACKER_1" = NULL
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
	case 2
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHEET_SKID_STACKER_2" = NULL
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
	case else
	UPDATE "LINE_CURRENT_STATUS"  
     SET "SHEET_SKID_NUM" = NULL
   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
 	;
end choose
	
//	
//	
//	UPDATE "LINE_CURRENT_STATUS"  
//     SET "SHEET_SKID_NUM" = NULL
//   WHERE "LINE_CURRENT_STATUS"."LINE_NUM" = :ii_line_id
// 	;
	if sqlca.sqlcode <> 0 then
		sqlca.of_rollback( )
		return
	else
		sqlca.of_commit( )
	end if
end if

end subroutine

on w_da_sheet.create
int iCurrent
call super::create
this.uo_mtlb4feed=create uo_mtlb4feed
this.hpb_coil=create hpb_coil
this.cb_exit=create cb_exit
this.ole_1=create ole_1
this.st_4=create st_4
this.st_time=create st_time
this.st_msg=create st_msg
this.cb_operatioin=create cb_operatioin
this.tab_queue=create tab_queue
this.cb_change_coil=create cb_change_coil
this.cb_1=create cb_1
this.st_efficiency=create st_efficiency
this.st_3=create st_3
this.rb_reject=create rb_reject
this.rb_feed=create rb_feed
this.pb_rejectlength=create pb_rejectlength
this.pb_feedlength=create pb_feedlength
this.em_reject_length=create em_reject_length
this.em_feed_length=create em_feed_length
this.uo_coilinfeeder=create uo_coilinfeeder
this.uo_noauto=create uo_noauto
this.uo_autorunning=create uo_autorunning
this.uo_automode=create uo_automode
this.st_goodpartcnt=create st_goodpartcnt
this.st_scrapcnt=create st_scrapcnt
this.st_partno=create st_partno
this.st_stroke=create st_stroke
this.st_10=create st_10
this.st_9=create st_9
this.st_8=create st_8
this.st_7=create st_7
this.st_6=create st_6
this.st_5=create st_5
this.st_1=create st_1
this.tab_da=create tab_da
this.gb_1=create gb_1
this.st_2=create st_2
this.st_shift=create st_shift
this.uo_sql=create uo_sql
this.uo_opc=create uo_opc
this.uo_plc=create uo_plc
this.st_job=create st_job
this.st_coil=create st_coil
this.dw_shift_coil=create dw_shift_coil
this.cb_end_coil=create cb_end_coil
this.st_yield=create st_yield
this.st_coil_finish_percent=create st_coil_finish_percent
iCurrent=UpperBound(this.Control)
this.Control[iCurrent+1]=this.uo_mtlb4feed
this.Control[iCurrent+2]=this.hpb_coil
this.Control[iCurrent+3]=this.cb_exit
this.Control[iCurrent+4]=this.ole_1
this.Control[iCurrent+5]=this.st_4
this.Control[iCurrent+6]=this.st_time
this.Control[iCurrent+7]=this.st_msg
this.Control[iCurrent+8]=this.cb_operatioin
this.Control[iCurrent+9]=this.tab_queue
this.Control[iCurrent+10]=this.cb_change_coil
this.Control[iCurrent+11]=this.cb_1
this.Control[iCurrent+12]=this.st_efficiency
this.Control[iCurrent+13]=this.st_3
this.Control[iCurrent+14]=this.rb_reject
this.Control[iCurrent+15]=this.rb_feed
this.Control[iCurrent+16]=this.pb_rejectlength
this.Control[iCurrent+17]=this.pb_feedlength
this.Control[iCurrent+18]=this.em_reject_length
this.Control[iCurrent+19]=this.em_feed_length
this.Control[iCurrent+20]=this.uo_coilinfeeder
this.Control[iCurrent+21]=this.uo_noauto
this.Control[iCurrent+22]=this.uo_autorunning
this.Control[iCurrent+23]=this.uo_automode
this.Control[iCurrent+24]=this.st_goodpartcnt
this.Control[iCurrent+25]=this.st_scrapcnt
this.Control[iCurrent+26]=this.st_partno
this.Control[iCurrent+27]=this.st_stroke
this.Control[iCurrent+28]=this.st_10
this.Control[iCurrent+29]=this.st_9
this.Control[iCurrent+30]=this.st_8
this.Control[iCurrent+31]=this.st_7
this.Control[iCurrent+32]=this.st_6
this.Control[iCurrent+33]=this.st_5
this.Control[iCurrent+34]=this.st_1
this.Control[iCurrent+35]=this.tab_da
this.Control[iCurrent+36]=this.gb_1
this.Control[iCurrent+37]=this.st_2
this.Control[iCurrent+38]=this.st_shift
this.Control[iCurrent+39]=this.uo_sql
this.Control[iCurrent+40]=this.uo_opc
this.Control[iCurrent+41]=this.uo_plc
this.Control[iCurrent+42]=this.st_job
this.Control[iCurrent+43]=this.st_coil
this.Control[iCurrent+44]=this.dw_shift_coil
this.Control[iCurrent+45]=this.cb_end_coil
this.Control[iCurrent+46]=this.st_yield
this.Control[iCurrent+47]=this.st_coil_finish_percent
end on

on w_da_sheet.destroy
call super::destroy
destroy(this.uo_mtlb4feed)
destroy(this.hpb_coil)
destroy(this.cb_exit)
destroy(this.ole_1)
destroy(this.st_4)
destroy(this.st_time)
destroy(this.st_msg)
destroy(this.cb_operatioin)
destroy(this.tab_queue)
destroy(this.cb_change_coil)
destroy(this.cb_1)
destroy(this.st_efficiency)
destroy(this.st_3)
destroy(this.rb_reject)
destroy(this.rb_feed)
destroy(this.pb_rejectlength)
destroy(this.pb_feedlength)
destroy(this.em_reject_length)
destroy(this.em_feed_length)
destroy(this.uo_coilinfeeder)
destroy(this.uo_noauto)
destroy(this.uo_autorunning)
destroy(this.uo_automode)
destroy(this.st_goodpartcnt)
destroy(this.st_scrapcnt)
destroy(this.st_partno)
destroy(this.st_stroke)
destroy(this.st_10)
destroy(this.st_9)
destroy(this.st_8)
destroy(this.st_7)
destroy(this.st_6)
destroy(this.st_5)
destroy(this.st_1)
destroy(this.tab_da)
destroy(this.gb_1)
destroy(this.st_2)
destroy(this.st_shift)
destroy(this.uo_sql)
destroy(this.uo_opc)
destroy(this.uo_plc)
destroy(this.st_job)
destroy(this.st_coil)
destroy(this.dw_shift_coil)
destroy(this.cb_end_coil)
destroy(this.st_yield)
destroy(this.st_coil_finish_percent)
end on

event pfc_postopen;call super::pfc_postopen;string ls_devicename, ls_line, ls_stacker, ls_devicename_stacker
int li_line_width
long ll_value, ll_shift
Real lr_feedlength, lr_rejectlength
string ls_msg = "Starting up!"
If not isValid(iw_please_wait) then
	openwithparm(iw_please_wait, ls_msg ,this)
end if

is_remotehost = ProfileString(gs_downtime_ini_file,"OPCItems","RemoteHost","localhost")
ls_devicename = ProfileString(gs_downtime_ini_file,"OPCItems","DeviceName","PLC5-BL110.")
ls_devicename_stacker = ProfileString(gs_downtime_ini_file,"StackerItem","DeviceName","")

ls_line = ProfileString(gs_downtime_ini_file,"DataBase","LogId","bl110")
string ls_passwd
ls_passwd = ProfileString(gs_downtime_ini_file,"DataBase","LogPassword","albl")
		gnv_logonattrib.ii_rc = 1
		gnv_logonattrib.is_userid = ls_line
		gnv_logonattrib.is_password = ls_passwd

//if ls_line="bl110" then
//	ii_line_id = 6
//elseif ls
is_user_name = ls_line
li_line_width = integer(mid(ls_line,3))
sqlca.of_connect( )	 	
		SELECT "LINE"."LINE_NUM"  
    		INTO :ii_line_id  
    	FROM "LINE"  
   	WHERE "LINE"."MAX_WIDTH" = :li_line_width;
if sqlca.sqlcode <> 0 then
	uo_sql.set_select( false)
	MessageBox("Database", "Error finding line id!")
else
	uo_sql.set_select( true)
	
end if

//Add OPC item in this section
is_coil = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_coil","abcoil")
is_job = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_job","abjob")
is_override = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_override","abshiftoverride")
is_shift = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_shift","abshift")
is_noauto = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_noauto","noauto-t")
is_automode = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_automode","automode")
is_autorunning = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_autorunning","autorunning")
is_coilinfeeder = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_coilinfeeder","coilinfeeder")
is_mtlb4feed = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_mtlb4feed","mtlb4feed")
//is_coilunload = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_coilunload","coilunload")
//is_coilunloadreset = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_coilunloadreset","coilunloadreset")
is_feedlength = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_feedlength","feedlength")
is_goodpartcnt = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_goodpartcnt","goodpartcnt")
is_rejectpartcnt = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_rejectpartcnt","rejectpartcnt")
is_rejectlength = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_rejectlength","rejectlength")
is_partno = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_partno","partno")
is_strokes = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_strokes","strokecnt")
is_skid= ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_skid","abskid")
is_feedreject= ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_feedreject","feedreject")
is_plc_fault = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_plc_fault","activefault")
is_opc_error = ls_devicename + ProfileString(gs_downtime_ini_file,"OPCItems","is_opc_error","Attributes._ErrorCode")
//Stacker OPC items
is_station2_stack_counter = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_station2_stack_counter","")
is_station1_stack_counter = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_station1_stack_counter","")
is_Sta2StackComplete = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_Sta2StackComplete","")
is_Sta1StackComplete = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_Sta1StackComplete","")
is_SendBlankToSta2 = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_SendBlankToSta2","")
is_SendBlankToSta1 = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_SendBlankToSta1","")
is_SlaveSta2ToSta1 = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_SlaveSta2ToSta1","")
is_stackleavingconveyor1 = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_StackLeavingConveyor1","")
is_StackLeavingSta2LiftTblConveyor = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_StackLeavingSta2LiftTblConveyor","")
is_StackLeavingSta1LiftTblConveyor = ls_devicename_stacker + ProfileString(gs_downtime_ini_file,"StackerItem","is_StackLeavingSta1LiftTblConveyor","")
//is_stackleavingconveyor1


//End of Stacker OPC items
ole_1.object.RemoteHost = is_remotehost
ole_1.object.Items.RemoveAll
ole_1.object.Items.AddItem(is_shift)
ole_1.object.Items.AddItem(is_coil)
ole_1.object.Items.AddItem(is_job)
ole_1.object.Items.AddItem(is_override)
ole_1.object.Items.AddItem(is_noauto)
ole_1.object.Items.AddItem(is_automode)
ole_1.object.Items.AddItem(is_autorunning)
ole_1.object.Items.AddItem(is_coilinfeeder)
ole_1.object.Items.AddItem(is_mtlb4feed)
ole_1.object.Items.AddItem(is_feedlength)
ole_1.object.Items.AddItem(is_goodpartcnt)
ole_1.object.Items.AddItem(is_rejectpartcnt)
ole_1.object.Items.AddItem(is_rejectlength)
ole_1.object.Items.AddItem(is_partno)
ole_1.object.Items.AddItem(is_strokes)
ole_1.object.Items.AddItem(is_skid)
ole_1.object.Items.AddItem(is_feedreject)
ole_1.object.Items.AddItem(is_opc_error )
ole_1.object.Items.AddItem(is_plc_fault )


//These are stacker items
if is_station2_stack_counter <> "" then ole_1.object.Items.AddItem(is_station2_stack_counter)
if is_station1_stack_counter <> "" then ole_1.object.Items.AddItem(is_station1_stack_counter)
if is_Sta2StackComplete <> "" then ole_1.object.Items.AddItem(is_Sta2StackComplete)
if is_Sta1StackComplete <> "" then ole_1.object.Items.AddItem(is_Sta1StackComplete)
if is_SendBlankToSta2 <> "" then ole_1.object.Items.AddItem(is_SendBlankToSta2)
if is_SendBlankToSta1 <> "" then ole_1.object.Items.AddItem(is_SendBlankToSta1)
if is_SlaveSta2ToSta1 <> "" then ole_1.object.Items.AddItem(is_SlaveSta2ToSta1)
if is_stackleavingconveyor1 <> "" then ole_1.object.Items.AddItem(is_stackleavingconveyor1 )
if is_StackLeavingSta2LiftTblConveyor <> "" then ole_1.object.Items.AddItem(is_StackLeavingSta2LiftTblConveyor)
if is_StackLeavingSta1LiftTblConveyor <> "" then ole_1.object.Items.AddItem(is_StackLeavingSta1LiftTblConveyor)

//End of OPC item section


is_scale_com_port = ProfileString(gs_downtime_ini_file,"OPCItems","scale_com_port","5")
is_skid_scale_online = ProfileString(gs_downtime_ini_file,"OPCItems","skid_scale_online","1")
is_scrap_scale_online = ProfileString(gs_downtime_ini_file,"OPCItems","scrap_scale_online","1")
is_scanner_online = ProfileString(gs_downtime_ini_file,"OPCItems","scanner_online","1")
ls_stacker = ProfileString(gs_downtime_ini_file,"OPCItems","stacker_online","1")

ii_com_port = Integer( is_scale_com_port ) - 1
if Integer(is_skid_scale_online) > 0 then
	ib_skid_scale_connected = true
end if

String ls_stack_length
ls_stack_length = ProfileString(gs_downtime_ini_file,"OPCItems","manualstacklength","60")

//if Integer(ls_stack_length) > 0 then
	ii_manual_stack_lenght = Integer(ls_stack_length)
	if ii_manual_stack_lenght < 1 then ii_manual_stack_lenght = 60
		
//end if

if Integer(is_scrap_scale_online) > 0 then
	ib_scrap_scale_connected = true
end if

if Integer(is_scanner_online) > 0 then
	ib_scanner_connected = true
else
	ib_scanner_connected = false
end if

if Integer(ls_stacker) > 0 then
	ib_stacker_online = true
else
	ib_stacker_online = false
end if


is_maintenance_password = ProfileString(gs_downtime_ini_file,"OPCItems","is_maintenance_password","5678")
is_prod_control_password = ProfileString(gs_downtime_ini_file,"OPCItems","is_prod_control_password","2345")
is_shift_super_password = ProfileString(gs_downtime_ini_file,"OPCItems","is_shift_super_password","1234")



if ole_1.object.Connect = false then
	MessageBox( "OPC connect", "Fail to connect opc server!", StopSign!, OK!)
	HALT CLOSE
end if
ole_1.object.Refresh
sleep(3)
ole_1.object.Refresh


tab_queue.tabpage_coil_queue.dw_coil_queue.of_settransobject(sqlca)
//tab_queue.tabpage_job_queue.dw_job_queue.settransobject(sqlca)

//long ll_shift,ll_value

//boolean lb_value

	il_abjob = ole_1.object.items(is_job ).value
	st_job.text = string( il_abjob )
	
	ib_feedreject = ole_1.object.items(is_feedreject).value
	//MessageBox("feedreject", string( ib_feedreject))
		
ib_automode = ole_1.object.items(is_automode ).value
uo_automode.set_select(ib_automode)
		
ib_autorunning = ole_1.object.items(is_autorunning ).value
//		if isValid(iuo_shift) then
//		 	if ib_autorunning then 
//				iuo_shift.postevent( "ue_line_running")
//			else
//				iuo_shift.postevent( "ue_line_idle")
//			end if
//		end if
uo_autorunning.set_select(ib_autorunning)


ib_coilinfeeder = ole_1.object.items(is_coilinfeeder ).value
ib_mtlb4feed = ole_1.object.items(is_mtlb4feed ).value
uo_coilinfeeder.set_select( ib_coilinfeeder )
uo_mtlb4feed.set_select( ib_mtlb4feed )

		if ii_line_id = 7 then
			lr_feedlength = Real(ole_1.object.items(is_feedlength ).value) / 100
		else
			lr_feedlength = Real(ole_1.object.items(is_feedlength ).value)
		end if
		em_feed_length.text = string( lr_feedlength )

il_override = ole_1.object.items(is_override ).value

il_goodpartcnt = ole_1.object.items(is_goodpartcnt ).value
st_goodpartcnt.text = string( il_goodpartcnt )

il_partno = ole_1.object.items(is_partno ).value
st_partno.text = string( il_partno )


		if ii_line_id = 7 then
			lr_rejectlength = Real(ole_1.object.items(is_rejectlength ).value) / 100
		else
			lr_rejectlength = Real(ole_1.object.items(is_rejectlength ).value)
		end if
		em_reject_length.text = string( lr_rejectlength )

		il_rejectpartcnt = ole_1.object.items(is_rejectpartcnt ).value
		st_scrapcnt.text = string( il_rejectpartcnt )
		
il_strokes = ole_1.object.items(is_strokes ).value
st_stroke.text = string( il_strokes )

		il_abcoil = ole_1.object.items(is_coil ).value
		st_coil.text = string(ll_value )
		
		ib_noauto = ole_1.object.items(is_noauto ).value
		uo_noauto.set_select( not ib_noauto )
		
		il_opc_error = ole_1.object.items(is_opc_error).value
		if il_opc_error > 0  then 
			uo_opc.set_select( false)
		else
			uo_opc.set_select( true)
		end if
		
		ii_plc_fault = ole_1.object.items(is_plc_fault ).value
		if ii_plc_fault > 0 then
			uo_plc.set_select( false)
		else
			uo_plc.set_select( true)
		end if
		if is_station1_stack_counter<>"" then il_station1_stack_counter = ole_1.object.items(is_station1_stack_counter ).value
		if is_station2_stack_counter<>"" then il_station2_stack_counter = ole_1.object.items(is_station1_stack_counter ).value
		if is_Sta2StackComplete<>"" then ib_Sta2StackComplete = ole_1.object.items(is_Sta2StackComplete ).value
		if is_Sta1StackComplete<>"" then ib_Sta1StackComplete = ole_1.object.items( is_Sta1StackComplete).value
		if is_SendBlankToSta2<>"" then ib_SendBlankToSta2 = ole_1.object.items(is_SendBlankToSta2 ).value
		if is_SendBlankToSta1<>"" then ib_SendBlankToSta1 = ole_1.object.items(is_SendBlankToSta1 ).value
		if is_SlaveSta2ToSta1<>"" then ib_SlaveSta2ToSta1 = ole_1.object.items( is_SlaveSta2ToSta1).value
		//MessageBox(string( il_opc_error ), string(ii_plc_fault ))
//	ll_shift = ole_1.object.items(is_shift).value
//		if ll_shift > 5000 then
//			if wf_new_shift( ll_shift ) <> 0 then
//				MessageBox("Database", " No shift availabe! ")
//				HALT CLOSE
//			end if
//		END IF		

iuo_db = create uo_seven_days_timer
iuo_db.of_register( this, "ue_reset_db", 1, 16:40:00)
itmg_refresh_display = create n_tmg
itmg_refresh_display.of_setsingle( true)
itmg_refresh_display.inv_single.of_register( this, "ue_refresh_display", 5)
iuo_timing_get_shift = create uo_timing_get_shift
iuo_timing_get_shift.init(this)

iuo_timing_get_shift.start(120)
iuo_timing_get_shift.event timer( )

If isValid(iw_please_wait) then
	close( iw_please_wait )
end if
st_msg.text = "Connection on line"
ib_post_open = true

end event

event close;call super::close;//MessageBox("Close", "Closing...")
//ole_1.object.disconnect
//if isValid( iw_override ) then
//	Close(iw_override )
//end if
//if isValid( iw_please_wait ) then
//	Close(iw_please_wait )
//end if
//if isValid( iw_operation_panel ) then
//	Close( iw_operation_panel )
//end if
	
	
//if isValid( iw_dt_log ) then
//	Close(iw_dt_log )
//end if

//if isValid( iw_maintenance ) then
//	Close(iw_maintenance )
//end if
//if isValid( iw_maintenance_validation ) then
//	Close(iw_maintenance_validation )
//end if
if isValid( iw_super_validation ) then
	Close(iw_super_validation )
end if



end event

event open;call super::open;ib_good_part_as_reject = false
ib_shift_ending = false
ib_coil_ending = false

if gi_screen = 2 then 
	//iw_production_folder_shop = message.powerobjectparm
	iw_production_folder_shop = gnv_app.iw_window
	if not isValid(iw_production_folder_shop) then
		MessageBox("Error", "Errow open ejob folder")
	else
//		messageBox("Test", iw_production_folder_shop.classname( ) )
	end if
end if
//ib_skid_scale_connected = true
//new interface
//tab_da.tabpage_maintenance.of_init( this)



//--------------------------No used-------------
//ole_1.object.Refresh
//ole_1.object.update

//tab_lines.tabpage_110.of_set_opc( ole_1 )
//tab_lines.tabpage_84.of_set_opc( ole_1 )

/*-----------------------------------------------------------------
ll_value = ole_1.object.items(is_job ).value
//st_job.text = string(il_abjob )
//this.postevent( "ue_job_changed", 0, ll_value )
ib_automode = ole_1.object.items(is_automode ).value
//st_automode.text = string(ib_automode )
uo_automode.set_select( ib_automode )
ib_autorunning = ole_1.object.items(is_autorunning ).value
//st_autorunning.text = string(ib_autorunning )
//st_autorunning.text = string(ole_1.object.items(is_autorunning ).value)
uo_autorunning.set_select( ib_autorunning )
ib_coilinfeeder = ole_1.object.items(is_coilinfeeder ).value
///st_coilinfeeder.text = string(ib_coilinfeeder )
uo_coilinfeeder.set_select( ib_coilinfeeder )
ib_coilunload = ole_1.object.items(is_coilunload ).value
//st_coilunload.text = string( ib_coilunload )
uo_coilunload.set_select( ib_coilunload )
il_feedlength = ole_1.object.items(is_feedlength ).value
st_feedlength.text = string( il_feedlength )
il_goodpartcnt = ole_1.object.items(is_goodpartcnt ).value
st_goodpartcnt.text = string( il_goodpartcnt )
il_partno = ole_1.object.items(is_partno ).value
st_partno.text = string( il_partno )
il_rejectlenght = ole_1.object.items(is_rejectlength ).value
st_scraplength.text = string( il_rejectlenght )
il_rejectpartcnt = ole_1.object.items(is_rejectpartcnt ).value
st_scrapcnt.text = string( il_rejectpartcnt )
ll_shift = long(ole_1.object.items(is_shift).value)
st_shift.text = string( ll_shift )
//sle_shift_id.text = string(ole_1.object.items(is_shift).value)
il_strokes = ole_1.object.items(is_strokes ).value
st_stroke.text = string( il_strokes )
ll_value = ole_1.object.items(is_coil ).value
//st_coil.text = string( il_abcoil )
//this.postevent( "ue_coil_changed", 0, ll_value)
ib_noauto = ole_1.object.items(is_noauto ).value
//st_noauto.text = string( ib_noauto )
uo_noauto.set_select( ib_noauto )
if ll_shift > 1000 then
	wf_new_shift( ll_shift)
end if
//-------------------------
*/

end event

event pfc_preclose;call super::pfc_preclose;//MessageBox("pfc_preclose","closing...")
ole_1.object.disconnect

if isValid(iuo_timing_get_shift) then
	if iuo_timing_get_shift.running then
		iuo_timing_get_shift.stop( )
	end if
	
	destroy iuo_timing_get_shift
end if

if isValid(iuo_db) then
	iuo_db.of_unregister( )
	destroy iuo_db
end if

if isValid( iuo_shift ) then
	destroy iuo_shift
end if

if isValid( iw_override ) then
	Close(iw_override )
end if
if isValid( iw_please_wait ) then
	Close(iw_please_wait )
end if
if isValid( iw_operation_panel ) then
	Close( iw_operation_panel )
end if


return 1
end event

type uo_mtlb4feed from u_line_status_button within w_da_sheet
integer x = 1814
integer y = 2291
integer width = 450
integer height = 74
integer taborder = 100
string caption = "Coil in feeder"
end type

on uo_mtlb4feed.destroy
call u_line_status_button::destroy
end on

type hpb_coil from u_hpb within w_da_sheet
integer x = 161
integer y = 445
integer width = 691
unsignedinteger position = 0
end type

type cb_exit from u_cb within w_da_sheet
integer x = 3354
integer y = 61
integer width = 351
integer height = 115
integer taborder = 200
integer textsize = -11
string facename = "Arial"
string text = "Exit"
end type

event clicked;call super::clicked;//long ll_rc
if isValid( iw_operation_panel ) then return 0
if this.of_messagebox( "id_Exit", "Confirm exit", "Do you want to exit?", Question!, YesNo!, 2) = 1 then
	if wf_super_validation( ) = 1 then
		parent.event pfc_close()
		HALT CLOSE
	end if
end if

end event

type ole_1 from olecustomcontrol within w_da_sheet
event onconnect ( )
event ondisconnect ( )
event ondatachanged ( integer bokay,  oleobject changeditems )
event onwritecompleted ( integer bokay,  oleobject writtenitems )
event onerror ( long ocx_error,  string description )
integer x = 834
integer y = 138
integer width = 176
integer height = 122
integer taborder = 30
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
string binarykey = "w_da_sheet.win"
integer textsize = -12
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
end type

event ondatachanged(integer bokay, oleobject changeditems);if not ib_post_open then
	return
end if
int li_count, li_i, li_temp
long ll_value
boolean lb_value
string ls_tag
Real lr_feedlength, lr_rejectlength
li_count = changeditems.count
Time lt_data_change_time
for li_i= 0 to li_count - 1
	ls_tag = changeditems.item(li_i).ItemId
	
	choose case ls_tag
		case is_coil
			il_abcoil = changeditems.item(li_i).value
			parent.st_coil.text = string(ll_value )
		case is_job
			il_abjob = changeditems.item(li_i).value
			parent.st_job.text = string( il_abjob )
		case is_automode
			ib_automode = changeditems.item(li_i).value
			uo_automode.set_select(ib_automode)
		case is_autorunning
			lb_value = changeditems.item(li_i).value
			if isValid(iuo_shift) then
		 		if lb_value then 
					iuo_shift.postevent( "ue_line_running")
				else
					iuo_shift.postevent( "ue_line_idle")
				end if
			end if
			ib_autorunning = lb_value
			uo_autorunning.set_select(ib_autorunning)
		case is_coilinfeeder
			ib_coilinfeeder = changeditems.item(li_i).value
			uo_coilinfeeder.set_select(ib_coilinfeeder)
			//------- auto end/start coil, may not work well!!
			if (not ib_coilinfeeder) and  isValid( iu_current_coil ) then
				if not cb_end_coil.enabled then cb_end_coil.enabled = true
				if not cb_end_coil.visible then cb_end_coil.visible = true
			end if
			//------end auto end/start, may not work well!! 
		case is_mtlb4feed
			ib_mtlb4feed = changeditems.item(li_i).value
			uo_mtlb4feed.set_select( ib_mtlb4feed )
			
			if (not ib_mtlb4feed) and isValid( iu_current_coil ) then
				if not cb_end_coil.enabled then cb_end_coil.enabled = true
				if not cb_end_coil.visible then cb_end_coil.visible = true
			end if
				
		case is_feedlength
			if ii_line_id = 7 then
				lr_feedlength = Real(changeditems.item(li_i).value) / 100
			else
				lr_feedlength = Real(changeditems.item(li_i).value)
			end if
			parent.em_feed_length.text = string( lr_feedlength )
			parent.em_feed_length.event modified( )
	
		case is_goodpartcnt
			il_goodpartcnt = changeditems.item(li_i).value
			parent.st_goodpartcnt.text = string( il_goodpartcnt )
			if isValid( iu_current_coil ) then 
				iu_current_coil.postevent( "ue_length_cutting", 0, il_goodpartcnt )
			end if
		case is_override
			il_override = changeditems.item(li_i).value
		case is_partno
			il_partno = changeditems.item(li_i).value
			parent.st_partno.text = string( il_partno )
		case is_rejectlength
			if ii_line_id = 7 then
				lr_rejectlength = Real(changeditems.item(li_i).value) / 100
			else
				lr_rejectlength = Real(changeditems.item(li_i).value)
			end if
			parent.em_reject_length.text = string( lr_rejectlength )
			em_reject_length.event modified( )
		case is_rejectpartcnt
			il_rejectpartcnt = changeditems.item(li_i).value
			parent.st_scrapcnt.text = string( il_rejectpartcnt )
			if isValid( iu_current_coil ) then 
				iu_current_coil.postevent( "ue_reject_cutting", 0, il_rejectpartcnt )
			end if
//		case is_shift
//			ll_value = changeditems.item(li_i).value
//			if ll_value > 1000 and ll_value > il_abshift then
//				if wf_new_shift( ll_value ) <> 0 then
//					MessageBox("Database", " No shift availabe! ")
//					continue
//				end if
//			elseif ll_value = -1 and il_abshift > 1000 then
//				wf_end_shift( )
//			else
//				// do nothing
//			end if
		case is_strokes
			il_strokes = changeditems.item(li_i).value
			parent.st_stroke.text = string( il_strokes )
		case is_noauto
			ib_noauto = changeditems.item(li_i).value
			if isValid(iuo_shift) then
				if ib_noauto then
					iuo_shift.postevent( "ue_noauto_set")
				else
					iuo_shift.postevent( "ue_noauto_clear")
				end if
			end if
			uo_noauto.set_select(not ib_noauto)
		case is_feedreject
			ib_feedreject = changeditems.item(li_i).value
//			if isValid( iu_current_coil ) then
//				if ib_feedreject then
//					iu_current_coil.of_select_skid_tabpg( )
//				else
//					iu_current_coil.of_select_scrap_tabpg( )
//				end if
//			end if
		case is_opc_error
			il_opc_error = changeditems.item(li_i).value
			if il_opc_error = 0 then
				uo_opc.set_select( true )
			else
				uo_opc.set_select( false)
			end if
		case is_plc_fault
			ii_plc_fault = changeditems.item(li_i).value
			if ii_plc_fault = 0 then
				uo_plc.set_select( true)
			else
				uo_plc.set_select( false)
			end if
		
		case is_StackLeavingSta2LiftTblConveyor //   is_Sta2StackComplete
			ib_Sta2StackComplete = changeditems.item(li_i).value
			ib_stackleavingsta2lifttblconveyor = ib_Sta2StackComplete
//			uo_plc.set_select( ib_sta2stackcomplete )
			if not ib_stacker_online then CONTINUE
			if ib_sta2stackcomplete then 
				if isValid( iu_current_coil ) then 
						iu_current_coil.postevent( "ue_station2_stack_complete")
					end if
			end if
		case is_StackLeavingSta1LiftTblConveyor  
			ib_Sta1StackComplete = changeditems.item(li_i).value
			ib_stackleavingsta1lifttblconveyor = ib_Sta1StackComplete
			if not ib_stacker_online then CONTINUE
			if ib_sta1stackcomplete then 
				if isValid( iu_current_coil ) then 
						iu_current_coil.postevent( "ue_station1_stack_complete")
					end if
			end if

     case is_Sta1StackComplete 
			// testing do nothing
	  case is_Sta2StackComplete
			// testing do nothing
		
		case is_station2_stack_counter
			if not ib_stacker_online then CONTINUE
			il_station2_stack_counter = changeditems.item(li_i).value
			if isValid( iu_current_coil ) then 
				if il_station2_stack_counter = 1 then
					iu_current_coil.postevent( "ue_station2_stack_start")
				elseif il_station2_stack_counter > 1 then
					iu_current_coil.postevent( "ue_stacker2_count", 0, il_station2_stack_counter )
				else
					//
				end if
			end if
		case is_station1_stack_counter
			if not ib_stacker_online then CONTINUE
			il_station1_stack_counter = changeditems.item(li_i).value
			if isValid( iu_current_coil ) then 
				if il_station1_stack_counter = 1 then 
					iu_current_coil.postevent( "ue_station1_stack_start")
				elseif il_station1_stack_counter > 1 then
					iu_current_coil.postevent( "ue_stacker1_count", 0, il_station1_stack_counter )
				else
					//
				end if
			end if
//		case is_Sta2StackComplete
//			ib_Sta2StackComplete = changeditems.item(li_i).value
////			uo_plc.set_select( ib_sta2stackcomplete )
//			if not ib_stacker_online then CONTINUE
//			if ib_sta2stackcomplete then 
//				if isValid( iu_current_coil ) then 
//						iu_current_coil.postevent( "ue_station2_stack_complete")
//					end if
//			end if
//		case is_Sta1StackComplete 
//			ib_Sta1StackComplete = changeditems.item(li_i).value
//			if not ib_stacker_online then CONTINUE
//			if ib_sta1stackcomplete then 
//				if isValid( iu_current_coil ) then 
//						iu_current_coil.postevent( "ue_station1_stack_complete")
//					end if
//			end if
//			
		case is_stackleavingconveyor1
			ib_StackLeavingConveyor1 = changeditems.item(li_i).value
			if ib_stackleavingconveyor1 then	
				if isValid( iu_current_coil ) then
					if isValid(iu_current_coil.of_get_skid_tab_page( )) then
						iu_current_coil.of_get_skid_tab_page( ).postevent( "ue_stack_leaving_conv1")
					end if
				end if
			end if
		case is_SendBlankToSta2 
			ib_SendBlankToSta2 = changeditems.item(li_i).value
			if not ib_stacker_online then CONTINUE
		case is_SendBlankToSta1 
			ib_SendBlankToSta1 = changeditems.item(li_i).value
			if not ib_stacker_online then CONTINUE
		case is_SlaveSta2ToSta1
			ib_SlaveSta2ToSta1 = changeditems.item(li_i).value
			if not ib_stacker_online then 
				CONTINUE
			else
				//if ib_slavesta2tosta1 then
				//uo_plc.set_select( ib_slavesta2tosta1 )
			end if
		case else
		//OPC error handling
	end choose
next

end event

event onerror(long ocx_error, string description);if isValid(iw_dt_change_job_reminder) then Close(iw_dt_change_job_reminder)
if isValid(iw_operation_panel ) then Close(iw_operation_panel)
if isValid( iw_override ) then Close(iw_override)
if isValid( iw_please_wait ) then Close(iw_please_wait )
if isValid( iw_super_validation ) then Close(iw_super_validation)


if parent.of_messagebox( "opc_error", "Error connecting to OPC server", "Serious error! Click ok to save your work and exit. Please call technical support!", StopSign!, OK! , 1) = 1 then
	parent.event pfc_close()
	HALT Close
end if
end event

type st_4 from statictext within w_da_sheet
integer x = 15
integer y = 1203
integer width = 936
integer height = 77
integer textsize = -7
integer weight = 700
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 16711680
long backcolor = 15780518
string text = "Processed Coils"
alignment alignment = center!
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_time from statictext within w_da_sheet
integer x = 48
integer y = 77
integer width = 194
integer height = 93
integer textsize = -12
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 134217730
long backcolor = 15780518
string text = "none"
boolean focusrectangle = false
end type

type st_msg from statictext within w_da_sheet
integer x = 2264
integer y = 2291
integer width = 786
integer height = 74
integer textsize = -10
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 67108864
string text = "none"
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type cb_operatioin from commandbutton within w_da_sheet
integer x = 2428
integer y = 61
integer width = 805
integer height = 115
integer taborder = 190
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
string text = "Operation Panel"
end type

event clicked;//if isValid( iuo_shift ) then
	openwithparm(iw_operation_panel, parent , parent)
//end if
end event

type tab_queue from tab within w_da_sheet
integer x = 15
integer y = 541
integer width = 936
integer height = 653
integer taborder = 60
integer textsize = -8
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long backcolor = 15780518
boolean raggedright = true
boolean focusonbuttondown = true
tabposition tabposition = tabsonleft!
integer selectedtab = 1
tabpage_coil_queue tabpage_coil_queue
end type

on tab_queue.create
this.tabpage_coil_queue=create tabpage_coil_queue
this.Control[]={this.tabpage_coil_queue}
end on

on tab_queue.destroy
destroy(this.tabpage_coil_queue)
end on

event selectionchanged;//if ib_post_open then
//	choose case newindex
//		case 2
//			this.tabpage_job_queue.dw_job_queue.retrieve( parent.ii_line_id )
//		case 1
			//messagebox("job",string(il_job))
			this.tabpage_coil_queue.dw_coil_queue.of_retrieve( )
//	end choose
//end if
//
end event

type tabpage_coil_queue from userobject within tab_queue
integer x = 117
integer y = 13
integer width = 805
integer height = 627
long backcolor = 15780518
string text = "Job Queue"
long tabtextcolor = 33554432
long tabbackcolor = 15780518
string picturename = "Form!"
long picturemaskcolor = 536870912
dw_coil_queue dw_coil_queue
end type

on tabpage_coil_queue.create
this.dw_coil_queue=create dw_coil_queue
this.Control[]={this.dw_coil_queue}
end on

on tabpage_coil_queue.destroy
destroy(this.dw_coil_queue)
end on

type dw_coil_queue from u_dw within tabpage_coil_queue
integer width = 805
integer height = 627
integer taborder = 101
string dataobject = "d_job_schedule_small"
boolean ib_isupdateable = false
end type

event pfc_retrieve;call super::pfc_retrieve;//long ll_shift
//if isValid(iuo_shift) then
//	ll_shift = iuo_shift.get_shift_id( )
//	return retrieve(ll_shift )
//else
//	return 0
//end if

//long ll_shift
//int li_line_id
//li_line_id = ii_line_id

if ii_line_id > 0 and ii_line_id < 10 then
	return retrieve( ii_line_id )
else
	return 0
end if


end event

event constructor;call super::constructor;this.of_settransobject( sqlca)
end event

event doubleclicked;call super::doubleclicked;return this.of_retrieve( )
end event

event rbuttondown;return 0
end event

event rbuttonup;return 0
end event

type cb_change_coil from commandbutton within w_da_sheet
boolean visible = false
integer x = 22
integer y = 157
integer width = 234
integer height = 77
integer taborder = 110
integer textsize = -6
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
boolean italic = true
string text = "New coil"
end type

event clicked;if not isValid( iuo_shift ) then
	MessageBox("Wait", "Not a valid shift", StopSign!)
	return 0
end if


if iuo_shift.of_get_coil_ending( ) then
	return 0
end if

if MessageBox("New coil loaded", "Do you want to select new change coil?", Exclamation!, OKCancel!, 2) = 2 then
		return 0
	end if

if isValid(iu_current_coil) then
	MessageBox("Coil status and weight needed!","Ending coil id " + iu_current_coil.get_coil_org_id( ))
	if not isValid( iu_current_coil ) then
		return 0
	end if
	
	
	OpenWithParm( w_recap, iu_current_coil, parent )
//	if not isValid( iu_current_coil ) then
//		return 0
//	end if
//	
//	
//	if iu_current_coil.get_new_status( ) <> 1 then
//		wf_done_coil()
//		iuo_shift.event ue_coil_online( )
//	else
//		MessageBox("Coil# " + iu_current_coil.get_coil_org_id( ), "You didn't change coil!")
//		return
//	end if
else
	parent.event ue_coil_online( )
end if
	





end event

type cb_1 from commandbutton within w_da_sheet
integer x = 3705
integer y = 496
integer width = 55
integer height = 45
integer taborder = 70
integer textsize = -12
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
string text = "."
end type

event clicked;//MessageBox("Coil in feeder",string(ib_mtlb4feed ))
//MessageBox("stacker", is_sta2stackcomplete)
if isValid( iu_current_coil ) then
	MessageBox("id:"+ string(iu_current_coil.get_coil_abc_id( )), "status:"+string( iu_current_coil.get_coil_shift_status( ) )&
	+ " nw:" + string(iu_current_coil.get_old_nw( ) ) &
	+ " nw_bal:" + string(iu_current_coil.get_nw_bal( ) ) &
	+ " density:" + string(iu_current_coil.of_get_densit( ) ) &
	+ " gauge:" + string(iu_current_coil.of_get_gauge( ) ) &
	+ " width:" + string(iu_current_coil.of_get_width( ) ) &
	+ " job:" + string(iu_current_coil.get_current_job( ) ) &
	+ " yield:" + string(iu_current_coil.of_get_yield( ) ) &
	+ " feedlength:" + string(iu_current_coil.of_get_feedlength( ) ) &
	+ " rejlenght:" + string(iu_current_coil.of_get_rejectlength( ) ) &
	)
	
end if
end event

type st_efficiency from statictext within w_da_sheet
integer x = 2114
integer y = 77
integer width = 230
integer height = 96
integer textsize = -12
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "0%"
alignment alignment = right!
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_3 from statictext within w_da_sheet
integer x = 22
integer y = 384
integer width = 150
integer height = 77
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "Coil:"
boolean focusrectangle = false
end type

type rb_reject from radiobutton within w_da_sheet
integer x = 1657
integer y = 403
integer width = 421
integer height = 80
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Reject"
end type

event clicked;ib_good_part_as_reject = true
end event

type rb_feed from radiobutton within w_da_sheet
integer x = 1657
integer y = 282
integer width = 380
integer height = 80
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Normal"
boolean checked = true
end type

event clicked;ib_good_part_as_reject = false
end event

type pb_rejectlength from u_pb within w_da_sheet
integer x = 3599
integer y = 403
integer width = 135
integer height = 96
integer taborder = 160
boolean originalsize = false
string picturename = "Compute!"
string disabledname = "Close!"
end type

event clicked;call super::clicked;em_reject_length.Event pfc_DDCalculator( )
end event

type pb_feedlength from u_pb within w_da_sheet
integer x = 3599
integer y = 243
integer width = 135
integer height = 96
integer taborder = 80
boolean originalsize = false
string picturename = "Compute!"
string disabledname = "Close!"
end type

event clicked;call super::clicked;em_feed_length.Event pfc_DDCalculator( )
end event

type em_reject_length from u_em within w_da_sheet
integer x = 3350
integer y = 403
integer width = 249
integer height = 96
integer taborder = 150
integer textsize = -11
fontcharset fontcharset = ansi!
string facename = "Arial"
string mask = "####.00"
end type

event constructor;call super::constructor;this.of_setdropdowncalculator( true)
this.iuo_calculator.of_setcloseonclick( true)
this.iuo_calculator.of_setinitialvalue( true)
end event

event modified;call super::modified;if isValid( iu_current_coil ) then
	//iu_current_coil.of_update_feed_length( Real(this.text)  )
	//iu_current_coil.of_update_reject_lenght( Real(this.text))
	iu_current_coil.of_update_reject_length( Real(this.text))
end if

end event

type em_feed_length from u_em within w_da_sheet
integer x = 3350
integer y = 243
integer width = 249
integer height = 96
integer taborder = 170
integer textsize = -11
fontcharset fontcharset = ansi!
string facename = "Arial"
string mask = "####.00"
end type

event constructor;call super::constructor;this.of_setdropdowncalculator( true)
this.iuo_calculator.of_setcloseonclick( true)
this.iuo_calculator.of_setinitialvalue( true)
end event

event modified;call super::modified;//MessageBOX("modified", string(this.classname( )))
if isValid( iu_current_coil ) then
	//iu_current_coil.of_update_feed_length( Real(this.text)  )
	Real lr_lenght
	lr_lenght = Real(this.text)
	if iu_current_coil.of_get_finish_percent( ) > 10  and ABS(iu_current_coil.of_get_part_length( ) - lr_lenght) > 0.5 then
		 Open( iu_current_coil.iw_dt_change_job_reminder,parent )
	end if
	iu_current_coil.of_update_feed_lenght( lr_lenght )
end if
	
end event

type uo_coilinfeeder from u_line_status_button within w_da_sheet
integer x = 1360
integer y = 2291
integer width = 450
integer height = 74
integer taborder = 90
string caption = "Coil on table"
end type

on uo_coilinfeeder.destroy
call u_line_status_button::destroy
end on

type uo_noauto from u_line_status_button within w_da_sheet
integer x = 914
integer y = 2291
integer width = 450
integer height = 74
integer taborder = 40
string caption = "Enable Auto"
end type

on uo_noauto.destroy
call u_line_status_button::destroy
end on

type uo_autorunning from u_line_status_button within w_da_sheet
integer x = 464
integer y = 2291
integer width = 450
integer height = 74
integer taborder = 30
string caption = "Auto running"
end type

on uo_autorunning.destroy
call u_line_status_button::destroy
end on

type uo_automode from u_line_status_button within w_da_sheet
integer x = 15
integer y = 2291
integer width = 450
integer height = 74
integer taborder = 10
string caption = "Master Auto"
end type

on uo_automode.destroy
call u_line_status_button::destroy
end on

type st_goodpartcnt from statictext within w_da_sheet
integer x = 1295
integer y = 256
integer width = 208
integer height = 96
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "Good part:"
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_scrapcnt from statictext within w_da_sheet
integer x = 1295
integer y = 403
integer width = 208
integer height = 96
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "Scrap:"
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_partno from statictext within w_da_sheet
integer x = 2651
integer y = 256
integer width = 208
integer height = 96
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "Part No:"
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_stroke from statictext within w_da_sheet
integer x = 2651
integer y = 403
integer width = 208
integer height = 96
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "Stroke:"
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_10 from statictext within w_da_sheet
integer x = 2253
integer y = 259
integer width = 391
integer height = 93
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Part Number"
boolean focusrectangle = false
end type

type st_9 from statictext within w_da_sheet
integer x = 2926
integer y = 403
integer width = 479
integer height = 77
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Reject Length:"
boolean focusrectangle = false
end type

type st_8 from statictext within w_da_sheet
integer x = 2926
integer y = 256
integer width = 439
integer height = 77
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Feed Length:"
boolean focusrectangle = false
end type

type st_7 from statictext within w_da_sheet
integer x = 962
integer y = 403
integer width = 391
integer height = 77
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Reject part:"
boolean focusrectangle = false
end type

type st_6 from statictext within w_da_sheet
integer x = 962
integer y = 256
integer width = 366
integer height = 77
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Good part:"
boolean focusrectangle = false
end type

type st_5 from statictext within w_da_sheet
integer x = 2253
integer y = 410
integer width = 391
integer height = 77
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Shift Strokes:"
boolean focusrectangle = false
end type

type st_1 from statictext within w_da_sheet
integer x = 1715
integer y = 77
integer width = 388
integer height = 77
integer textsize = -12
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "Efficiency:"
boolean focusrectangle = false
end type

type tab_da from u_da_tab within w_da_sheet
integer x = 965
integer y = 541
integer width = 2794
integer height = 1757
integer taborder = 50
end type

event clicked;call super::clicked;//return 0
//return 0
//
//if not isValid( iuo_shift ) or il_abjob < 1000 then 
//	return 0
//end if
//
//if isValid(iu_current_coil) then
//	if isValid( iu_current_coil.of_get_tab_page( ) ) then
//		CHOOSE CASE index
//		CASE 2
//      	wf_show_dw_process_status( )
//			wf_show_shift_coil( )
//		//parent.show_dw_shift_coil( )
//		//parent.show_dw_process_status( )
//		CASE 3
//      		wf_show_job_sheet( )		  
//		CASE 4
//      		wf_show_report( )
//		CASE 5
//			wf_show_dt_report_summary( )
//		CASE ELSE
//	end choose
//		
//	end if
//else
//	CHOOSE CASE index
//		CASE 1
//      	wf_show_dw_process_status( )
//			wf_show_shift_coil( )
//		//parent.show_dw_shift_coil( )
//		//parent.show_dw_process_status( )
//		CASE 2
//      		wf_show_job_sheet( )		  
//		CASE 3
//      		wf_show_report( )
//		CASE 4
//			wf_show_dt_report_summary( )
//		CASE 5
//		//parent.show_report_summary( )
//		CASE ELSE
//	end choose
//end if
//
//
end event

type gb_1 from groupbox within w_da_sheet
integer x = 1627
integer y = 202
integer width = 571
integer height = 307
integer taborder = 130
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Feed cut as:"
end type

type st_2 from statictext within w_da_sheet
integer x = 15
integer y = 256
integer width = 366
integer height = 77
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 8388608
long backcolor = 15780518
string text = "Job Num:"
boolean focusrectangle = false
end type

type st_shift from statictext within w_da_sheet
integer x = 278
integer y = 77
integer width = 1488
integer height = 93
integer textsize = -12
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "shift_id"
boolean focusrectangle = false
end type

type uo_sql from u_fault_status_button within w_da_sheet
integer x = 3050
integer y = 2291
integer width = 234
integer height = 74
integer taborder = 130
string caption = "ORA"
end type

on uo_sql.destroy
call u_fault_status_button::destroy
end on

type uo_opc from u_fault_status_button within w_da_sheet
integer x = 3280
integer y = 2291
integer width = 234
integer height = 74
integer taborder = 140
string caption = "OPC"
end type

on uo_opc.destroy
call u_fault_status_button::destroy
end on

type uo_plc from u_fault_status_button within w_da_sheet
integer x = 3522
integer y = 2291
integer width = 234
integer height = 74
integer taborder = 150
string caption = "PLC"
end type

on uo_plc.destroy
call u_fault_status_button::destroy
end on

type st_job from statictext within w_da_sheet
integer x = 307
integer y = 253
integer width = 538
integer height = 106
boolean bringtotop = true
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "Job Num:"
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_coil from statictext within w_da_sheet
integer x = 161
integer y = 352
integer width = 402
integer height = 96
boolean bringtotop = true
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "None"
alignment alignment = right!
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type dw_shift_coil from u_dw within w_da_sheet
integer x = 15
integer y = 1267
integer width = 936
integer height = 1024
integer taborder = 100
boolean bringtotop = true
string dataobject = "d_shift_coil"
boolean ib_isupdateable = false
end type

event clicked;call super::clicked;of_retrieve( )
return 1
//if row > 0 then
//	this.selectrow( 0, false)
//	this.selectrow( row, true)
//end if
end event

event constructor;call super::constructor;//this.of_setdropdowncalculator( true)
//this.iuo_calculator.of_register("shift_coil_coil_end_wt", this.iuo_calculator.none )
//this.iuo_calculator.of_setcloseonclick( true)
//this.iuo_calculator.of_setinitialvalue( true)
this.of_settransobject( sqlca )

end event

event itemchanged;call super::itemchanged;//if row > 0 then
//	if dwo.Name = "shift_coil_coil_end_wt" then
//		long ll_end_wt, ll_process_wt, ll_init_wt, ll_shift
//		int li_run
//		ll_end_wt = Long(data)
//		ll_init_wt = this.object.shift_coil_coil_begin_wt[row]		
//		ll_process_wt = ll_init_wt - ll_end_wt
//		ll_shift = this.object.shift_coil_shift_num[row]
//		li_run = this.object.shift_coil_coil_run_num[row]
//		  
//		  UPDATE "SHIFT_COIL"  
//     		SET "COIL_BEGIN_WT" = :ll_init_wt,   
//         "COIL_END_WT" = :ll_end_wt,   
//         "PROCESS_WT" = :ll_process_wt  
//   WHERE ( "SHIFT_COIL"."SHIFT_NUM" = :ll_shift ) AND  
//         ( "SHIFT_COIL"."COIL_RUN_NUM" =:li_run  )   ;
//		if sqlca.sqlcode <> 0 then
//			sqlca.of_rollback( )
//			return 1
//		else
//			sqlca.of_commit( )
//		end if
//		this.object.shift_coil_process_wt[row] = ll_process_wt
//			return 0
//	else
//		return 1
//	end if
//end if
end event

event pfc_retrieve;call super::pfc_retrieve;long ll_shift_id
if isValid( iuo_shift ) then
	ll_shift_id = iuo_shift.get_shift_id( )
	return this.retrieve( ll_shift_id )
else
	return -1
end if
end event

event rbuttondown;return 0
end event

event rbuttonup;return 0
end event

type cb_end_coil from commandbutton within w_da_sheet
string tag = "end current coil or change job"
boolean visible = false
integer x = 563
integer y = 349
integer width = 289
integer height = 96
integer taborder = 190
integer textsize = -9
integer weight = 700
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
boolean enabled = false
string text = "End coil"
end type

event clicked;//if isValid(iu_current_coil) then
//	if MessageBox("Coil num "+iu_current_coil.get_coil_org_id( ), "Do you want to end this coil " + iu_current_coil.get_coil_org_id( )+ "?", Exclamation!, OKCancel!, 2) = 2 then
//		return 0
//	end if
//	
//	if not isValid( iu_current_coil ) then
//		return 0
//	end if
//	
//	OpenWithParm( w_recap, iu_current_coil, parent )
//end if
	ii_event_id = endcoil
	wf_open_operation_panel( )
end event

type st_yield from statictext within w_da_sheet
integer x = 563
integer y = 352
integer width = 289
integer height = 96
integer textsize = -10
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 255
long backcolor = 15780518
boolean enabled = false
string text = " "
alignment alignment = right!
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type

type st_coil_finish_percent from statictext within w_da_sheet
boolean visible = false
integer x = 622
integer y = 352
integer width = 230
integer height = 96
integer textsize = -11
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 15780518
string text = "0%"
alignment alignment = right!
boolean border = true
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
end type


Start of PowerBuilder Binary Data Section : Do NOT Edit
0Aw_da_sheet.bin 
2000000e00e011cfd0e11ab1a1000000000000000000000000000000000003003e0009fffe000000060000000000000000000000010000000100000000000010000000000200000001fffffffe0000000000000000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffd00000004fffffffe00000005fffffffefffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff006f00520074006f004500200074006e00790072000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000050016ffffffffffffffff00000003000000000000000000000000000000000000000000000000000000007091c2d001d1e2af00000003000002800000000000500003004c004200430049004e0045004500530045004b000000590000000000000000000000000000000000000000000000000000000000000000000000000002001cffffffffffffffffffffffff00000000000000000000000000000000000000000000000000000000000000000000000000000000000000800000000000500003004f0042005800430054005300450052004d0041000000000000000000000000000000000000000000000000000000000000000000000000000000000002001affffffffffffffffffffffff00000000000000000000000000000000000000000000000000000000000000000000000000000002000000da00000000004200500043004f00530058004f00540041005200450047000000000000000000000000000000000000000000000000000000000000000000000000000000000101001a000000020000000100000004016d22bc11d72951a000c9b5073e3624000000007091c2d001d1e2af7091c2d001d1e2af00000000000000000000000000000001fffffffe000000030000000400000005fffffffe000000070000000800000009fffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
20ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff00630064006b006b00680063006e00640064006f006d00660065006e00620062006400680069006600650062006f006800670064006500670070006e0066006200610064006e006a00670062006600610065006d007000680069006a006600660062006d0070006100650066006600670063006700640070006c0064006600660000030000000003000800000000002c006900430051006d0065007500740073006e0049002e0063004700490050004f00410043002e0042000000310002000800000000001c000800310000003200390031002e003800360039002e0031002e0039003600030000000003e800000004000b00000003ffff00004e20ffff000bffff000b138800031971000000010508004400000073000000610074006b00630072006500310031002e00300074007300740061006f00690032006e0073005f00610074006b00630063005f0075006f0074006e00720065000000000000000000000000000000000000000000000000000000000000000000000000000000000000030000000003000800000000002c006900430051006d0065007500740073006e0049002e0063004700490050004f00410043002e0042000000310002000800000000001c000800310000003200390031002e003800360039002e0031002e0039003600030000000003e800000004000b00000003ffff00004e20ffff000b006f00430074006e006e00650073007400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001020012ffffffffffffffffffffffff00000000000000000000000000000000000000000000000000000000000000000000000000000006000000da000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffff0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffff0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffff000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffff000b138800031971000000010508004400000073000000610074006b00630072006500310031002e00300074007300740061006f00690032006e0073005f00610074006b00630063005f0075006f0074006e00720065000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000
1Aw_da_sheet.bin 
End of PowerBuilder Binary Data Section : No Source Expected After This Point
