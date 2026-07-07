$PBExportHeader$w_qc_sheet.srw
forward
global type w_qc_sheet from w_sheet
end type
type rb_current_shift from u_rb within w_qc_sheet
end type
type tab_dim_skid from u_tab within w_qc_sheet
end type
type tab_dim_skid from u_tab within w_qc_sheet
end type
type tab_job from u_tab_eval_job_select within w_qc_sheet
end type
type tab_job from u_tab_eval_job_select within w_qc_sheet
end type
type cb_select_job from u_cb within w_qc_sheet
end type
type rb_realtime from u_rb within w_qc_sheet
end type
type rb_offline from u_rb within w_qc_sheet
end type
type st_1 from statictext within w_qc_sheet
end type
type ole_1 from olecustomcontrol within w_qc_sheet
end type
type gb_1 from u_gb within w_qc_sheet
end type
end forward

global type w_qc_sheet from w_sheet
integer x = 0
integer y = 0
integer width = 3701
integer height = 2154
string title = "Quality check"
boolean maxbox = false
windowstate windowstate = maximized!
boolean center = true
event ue_reset_db ( )
rb_current_shift rb_current_shift
tab_dim_skid tab_dim_skid
tab_job tab_job
cb_select_job cb_select_job
rb_realtime rb_realtime
rb_offline rb_offline
st_1 st_1
ole_1 ole_1
gb_1 gb_1
end type
global w_qc_sheet w_qc_sheet

type variables
string is_skid = "abskid"
string is_job = "abjob"
string is_coil = "abcoil"
string is_remotehost = "lacalhost"
string is_shift = "abshift"
Integer ii_line_id
long il_job, il_coil, il_skid, il_shift
uo_seven_days_timer iuo_db
Boolean ib_post_open = false
n_ds ids_job_list
u_tabpg_eval_job_selection iu_eval_job
u_tabpg_skid_dim_check iu_tabpg_skid_dim_check
u_tabpg_job_dim_check_gr iu_tabpg_job_dim_check_gr
end variables

forward prototypes
public function long of_register_treeview (long al_job)
public subroutine of_realtime_change_skid (long al_skid)
public function integer of_register_treeview_shift (long al_shift)
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
end if

end event

public function long of_register_treeview (long al_job);long ll_row
ids_job_list.reset( )
ll_row = ids_job_list.insertrow( 0 )

//if ids_job_list.rowcount( ) < 1 then
//	ll_row = ids_job_list.insertrow( 0 )
//elseif ids_job_list.rowcount( ) = 1 then
//	ids_job_list.deleterow( 1)
//	ll_row = ids_job_list.insertrow( 0 )
//else
//	return -1
//end if


ids_job_list.object.ab_job[ll_row] = al_job
ids_job_list.object.ab_job_name[ll_row] = "Job " + string(al_job)

tab_job.closetab( iu_eval_job)
tab_job.opentabwithparm( iu_eval_job, this, 0 )

long ll_1
return ll_1
end function

public subroutine of_realtime_change_skid (long al_skid);tab_dim_skid.closetab( iu_tabpg_skid_dim_check)
tab_dim_skid.opentabwithparm( iu_tabpg_skid_dim_check, al_skid,0)
end subroutine

public function integer of_register_treeview_shift (long al_shift);long ll_row, ll_rc, ll_i
n_ds lds_job
lds_job = create n_ds
lds_job.dataobject = "d_jobs_for_shift"
lds_job.of_settransobject( sqlca)
lds_job.retrieve( al_shift )

ll_rc = lds_job.rowcount( )

if ll_rc < 1 then
	return 1
end if

ids_job_list.reset( )

for ll_i = 1 to ll_rc
	ll_row = ids_job_list.insertrow( 0 )
	ids_job_list.object.ab_job[ll_row] = lds_job.object.ab_job_num[ll_row]
	ids_job_list.object.ab_job_name[ll_row] = "Job " + string(lds_job.object.ab_job_num[ll_row])
next





//if ids_job_list.rowcount( ) < 1 then
//	ll_row = ids_job_list.insertrow( 0 )
//elseif ids_job_list.rowcount( ) = 1 then
//	ids_job_list.deleterow( 1)
//	
//else
//	return -1
//end if





//ids_job_list.object.ab_job[ll_row] = al_job
//ids_job_list.object.ab_job_name[ll_row] = "Job " + string(al_job)
//
tab_job.closetab( iu_eval_job)
tab_job.opentabwithparm( iu_eval_job, this, 0 )
//
//long ll_1
//return ll_1
return 1
end function

on w_qc_sheet.create
int iCurrent
call super::create
this.rb_current_shift=create rb_current_shift
this.tab_dim_skid=create tab_dim_skid
this.tab_job=create tab_job
this.cb_select_job=create cb_select_job
this.rb_realtime=create rb_realtime
this.rb_offline=create rb_offline
this.st_1=create st_1
this.ole_1=create ole_1
this.gb_1=create gb_1
iCurrent=UpperBound(this.Control)
this.Control[iCurrent+1]=this.rb_current_shift
this.Control[iCurrent+2]=this.tab_dim_skid
this.Control[iCurrent+3]=this.tab_job
this.Control[iCurrent+4]=this.cb_select_job
this.Control[iCurrent+5]=this.rb_realtime
this.Control[iCurrent+6]=this.rb_offline
this.Control[iCurrent+7]=this.st_1
this.Control[iCurrent+8]=this.ole_1
this.Control[iCurrent+9]=this.gb_1
end on

on w_qc_sheet.destroy
call super::destroy
destroy(this.rb_current_shift)
destroy(this.tab_dim_skid)
destroy(this.tab_job)
destroy(this.cb_select_job)
destroy(this.rb_realtime)
destroy(this.rb_offline)
destroy(this.st_1)
destroy(this.ole_1)
destroy(this.gb_1)
end on

event close;call super::close;ole_1.object.disconnect
end event

event pfc_postopen;call super::pfc_postopen;string ls_inifile, ls_devicename, ls_line
int li_line_width

ls_inifile = gs_downtime_ini_file
is_remotehost = ProfileString(ls_inifile,"OPCItems","RemoteHost","localhost")
ls_devicename = ProfileString(ls_inifile,"OPCItems","DeviceName","PLC5-BL110.")
ls_line = ProfileString(ls_inifile,"DataBase","LogId","bl110")

li_line_width = integer(mid(ls_line,3))
sqlca.of_connect( )	 	
		SELECT "LINE"."LINE_NUM"  
    		INTO :ii_line_id  
    	FROM "LINE"  
   	WHERE "LINE"."MAX_WIDTH" = :li_line_width;
if sqlca.sqlcode <> 0 then
	MessageBox("Database", "Error finding line id!")
end if


is_coil = ls_devicename + ProfileString(ls_inifile,"OPCItems","is_coil","abcoil")
is_job = ls_devicename + ProfileString(ls_inifile,"OPCItems","is_job","abjob")
is_skid= ls_devicename + ProfileString(ls_inifile,"OPCItems","is_skid","abskid")
is_shift = ls_devicename + ProfileString(ls_inifile,"OPCItems","is_shift","abshift")


ole_1.object.RemoteHost = is_remotehost
ole_1.object.Items.RemoveAll
ole_1.object.Items.AddItem(is_coil)
ole_1.object.Items.AddItem(is_job)
ole_1.object.Items.AddItem(is_skid)
ole_1.object.Items.AddItem(is_shift)

if ole_1.object.Connect = false then
	MessageBox( "OPC connect", "Fail to connect opc server!", StopSign!, OK!)
	HALT CLOSE
end if
ole_1.object.Refresh
sleep(3)
ole_1.object.Refresh

iuo_db = create uo_seven_days_timer
iuo_db.of_register( this, "ue_reset_db", 1, 16:40:00)

il_coil = ole_1.object.items( this.is_coil ).value
il_job = ole_1.object.items( this.is_job ).value
il_skid = ole_1.object.items( this.is_skid ).value
il_shift = ole_1.object.items(is_shift).value


//---------------------------------------------
long ll_row
ll_row = ids_job_list.insertrow( 0 )
ids_job_list.object.ab_job[ll_row] = il_job
ids_job_list.object.ab_job_name[ll_row] = "Job " + string(il_job)
tab_job.opentabwithparm( iu_eval_job, this, 0)

if rb_realtime.checked then
			of_realtime_change_skid( il_skid )
end if
ib_post_open = true
end event

event open;call super::open;ids_job_list = create n_ds
ids_job_list.dataobject = "d_qc_job_list"
end event

type rb_current_shift from u_rb within w_qc_sheet
integer x = 2033
integer y = 422
integer width = 724
integer textsize = -12
string facename = "Arial"
string text = "Job for current shift"
end type

event clicked;call super::clicked;
if il_shift < 1000 then
	MessageBox("Current shift", "No current shift available!")
	rb_offline.checked = true
	cb_select_job.enabled = true
	rb_current_shift.checked = false
	return 1
end if

of_register_treeview_shift( il_shift )
end event

type tab_dim_skid from u_tab within w_qc_sheet
integer x = 936
integer y = 653
integer width = 2706
integer height = 1370
integer taborder = 31
string facename = "Arial"
end type

type tab_job from u_tab_eval_job_select within w_qc_sheet
integer x = 59
integer y = 218
integer width = 805
integer height = 1818
integer taborder = 11
end type

type cb_select_job from u_cb within w_qc_sheet
integer x = 1390
integer y = 384
integer width = 453
integer height = 141
integer taborder = 21
integer textsize = -12
string facename = "Arial"
boolean enabled = false
string text = "Select Job"
end type

event clicked;call super::clicked;long ll_row, ll_job
Open(w_select_job)
ll_job = message.doubleparm
parent.of_register_treeview( ll_job)
end event

type rb_realtime from u_rb within w_qc_sheet
integer x = 2926
integer y = 422
integer width = 497
integer height = 64
integer textsize = -12
string facename = "Arial"
string text = "Current skid"
boolean checked = true
end type

event clicked;call super::clicked;if this.checked then
	cb_select_job.enabled = false
end if

if il_skid < 1000 then
	MessageBox("Invalid skid", "No current skid available!")
	rb_offline.checked = true
	cb_select_job.enabled = true
	rb_realtime.checked = false
	return 1
end if

of_register_treeview( il_job)

if isValid(iu_tabpg_job_dim_check_gr) then
	tab_dim_skid.closetab( iu_tabpg_job_dim_check_gr)
end if

if isValid(iu_tabpg_skid_dim_check) then
	if iu_tabpg_skid_dim_check.il_skid <> il_skid then
		tab_dim_skid.closetab( iu_tabpg_skid_dim_check )
		tab_dim_skid.opentabwithparm( iu_tabpg_skid_dim_check , il_skid, 0)
	end if
else
	tab_dim_skid.opentabwithparm( iu_tabpg_skid_dim_check , il_skid, 0)
end if

//of_register_treeview( il_job)
end event

type rb_offline from u_rb within w_qc_sheet
integer x = 1068
integer y = 422
integer textsize = -12
string facename = "Arial"
string text = "Offline"
end type

event clicked;call super::clicked;if this.checked then
	cb_select_job.enabled = true
end if
end event

type st_1 from statictext within w_qc_sheet
integer x = 1375
integer y = 102
integer width = 1214
integer height = 128
integer textsize = -16
integer weight = 700
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 16711680
long backcolor = 67108864
string text = "Skid dimenstional check"
boolean focusrectangle = false
end type

type ole_1 from olecustomcontrol within w_qc_sheet
event onconnect ( )
event ondisconnect ( )
event ondatachanged ( integer bokay,  oleobject changeditems )
event onwritecompleted ( integer bokay,  oleobject writtenitems )
event onerror ( long ocx_error,  string description )
integer x = 59
integer y = 26
integer width = 176
integer height = 122
integer taborder = 10
borderstyle borderstyle = stylelowered!
boolean focusrectangle = false
string binarykey = "w_qc_sheet.win"
integer textsize = -8
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

int li_count, li_i
long ll_value
string ls_tag
li_count = changeditems.count
for li_i= 0 to li_count - 1
	ls_tag = changeditems.item(li_i).ItemId
	if ls_tag = is_coil then
		il_coil = changeditems.item(li_i).value
	elseif ls_tag = is_job then
		il_job = changeditems.item(li_i).value
		if rb_realtime.checked then
			of_register_treeview( il_job)
		end if
	elseif ls_tag = is_skid then
		il_skid = changeditems.item(li_i).value
		if rb_realtime.checked then
			of_realtime_change_skid( il_skid )
		end if
	elseif ls_tag = is_shift then
		il_shift = changeditems.item(li_i).value
	else
		continue
	end if
next
		
end event

type gb_1 from u_gb within w_qc_sheet
integer x = 936
integer y = 282
integer width = 2589
integer height = 307
integer taborder = 11
integer textsize = -12
string facename = "Arial"
string text = "Operation mode"
end type


Start of PowerBuilder Binary Data Section : Do NOT Edit
09w_qc_sheet.bin 3992 
2000000e00e011cfd0e11ab1a1000000000000000000000000000000000003003e0009fffe000000060000000000000000000000010000000100000000000010000000000200000001fffffffe0000000000000000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffd00000004fffffffe00000005fffffffefffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff006f00520074006f004500200074006e00790072000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000050016ffffffffffffffff00000003000000000000000000000000000000000000000000000000000000004614011001d1b82200000003000003000000000000500003004c004200430049004e0045004500530045004b000000590000000000000000000000000000000000000000000000000000000000000000000000000002001cffffffffffffffffffffffff00000000000000000000000000000000000000000000000000000000000000000000000000000000000000800000000000500003004f0042005800430054005300450052004d0041000000000000000000000000000000000000000000000000000000000000000000000000000000000002001affffffffffffffffffffffff000000000000000000000000000000000000000000000000000000000000000000000000000000020000012a00000000004200500043004f00530058004f00540041005200450047000000000000000000000000000000000000000000000000000000000000000000000000000000000101001a000000020000000100000004016d22bc11d72951a000c9b5073e3624000000004614011001d1b8224614011001d1b82200000000000000000000000000000001fffffffe00000003000000040000000500000006fffffffe00000008000000090000000a0000000bfffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
20ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff00630064006b006b00680063006e00640064006f006d00660065006e00620062006400680069006600650062006f006800670064006500670070006e0066006200610064006e006a00670062006600610065006d007000680069006a006600660062006d0070006100650066006600670063006700640070006c0064006600660000030000000003000800000000002c006900430051006d0065007500740073006e0049002e0063004700490050004f00410043002e004200000031000200080000000000140008006c00000063006f006c0061006f00680074007300030000000003e800000004000b00000003ffff00004e20ffff000bffff000b13880003197100000004050800240000005000000043004c002d0035004c004200310031002e003000620061006f0063006c006900220000005000000043004c002d0035004c004200310031002e003000620061006f006a0000006200000024004c0050003500430042002d0031004c003000310061002e007300620069006b0000006400000026004c0050003500430042002d0031004c003000310061002e0073006200690068007400660000000000000000000000000000000000000000000000000000030000000003000800000000002c006900430051006d0065007500740073006e0049002e0063004700490050004f00410043002e00420000003100020008006f00430074006e006e00650073007400000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000001020012ffffffffffffffffffffffff000000000000000000000000000000000000000000000000000000000000000000000000000000070000012a000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffff0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffff0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000ffffffffffffffffffffffff0000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000140008006c00000063006f006c0061006f00680074007300030000000003e800000004000b00000003ffff00004e20ffff000bffff000b13880003197100000004050800240000005000000043004c002d0035004c004200310031002e003000620061006f0063006c006900220000005000000043004c002d0035004c004200310031002e003000620061006f006a0000006200000024004c0050003500430042002d0031004c003000310061002e007300620069006b0000006400000026004c0050003500430042002d0031004c003000310061002e007300620069006800740066000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000
19w_qc_sheet.bin 3992 
End of PowerBuilder Binary Data Section : No Source Expected After This Point
