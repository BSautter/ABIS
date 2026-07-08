$PBExportHeader$w_da_frame.srw
forward
global type w_da_frame from w_frame
end type
end forward

global type w_da_frame from w_frame
integer width = 3814
integer height = 2515
string title = "DAS"
string menuname = "m_ss_frame"
boolean controlmenu = false
boolean minbox = false
boolean maxbox = false
windowtype windowtype = mdi!
string icon = ".\jaguar.ico"
boolean toolbarvisible = false
toolbaralignment toolbaralignment = alignatbottom!
boolean center = true
end type
global w_da_frame w_da_frame

type variables
w_sheet iw_downtime_duration
w_sheet iw_downtime_cause
end variables

forward prototypes
public subroutine of_downtime_duration_opensheet ()
public subroutine of_opensheet_downtime_cause ()
public subroutine wf_set_ejob_window (readonly window aw_window)
end prototypes

public subroutine of_downtime_duration_opensheet ();OpenSheet(iw_downtime_duration, "w_da_downtime_duration", this, 0, Original!)
end subroutine

public subroutine of_opensheet_downtime_cause ();OpenSheet(iw_downtime_cause, "w_da_downtime_cause", this, 0, Original!)
end subroutine

public subroutine wf_set_ejob_window (readonly window aw_window);//
w_da_sheet lw_sheet

lw_sheet  = this.getactivesheet( )
if isValid(lw_sheet) then	
	lw_sheet.wf_set_ejob_window( aw_window )
else
	messagebox("Error", "Failed to setup ejob folder window!")
end if
end subroutine

event pfc_open;call super::pfc_open;w_sheet lw_sheet, lw_opensheets[ ]
String ls_sheet
Integer li_return
ls_sheet = Message.StringParm
li_return = this.inv_sheetmanager.of_GetSheetsByClass(lw_opensheets, ls_sheet)
IF li_return > 0 THEN
lw_opensheets[1].WindowState = Maximized!
lw_opensheets[1].BringToTop = TRUE
ELSE
OpenSheet(lw_sheet, ls_sheet, this, 0, Original!)
END IF
end event

on w_da_frame.create
call super::create
if IsValid(this.MenuID) then destroy(this.MenuID)
if this.MenuName = "m_ss_frame" then this.MenuID = create m_ss_frame
end on

on w_da_frame.destroy
call super::destroy
if IsValid(MenuID) then destroy(MenuID)
end on

event pfc_postopen;call super::pfc_postopen;Integer li_return
String ls_window
w_da_sheet lw_sheet
//gs_downtime_ini_file = gnv_app.of_getappinifile( )

ls_window = ProfileString(gs_downtime_ini_file,"WINDOW","default_window","w_da_sheet")
IF SQLca.OF_init( gs_downtime_ini_file, "Database") = -1 THEN
	MessageBox("Database", "Error in initialize file")
	HALT CLOSE
end if
//MessageBox("frame","pfc_postopen")

/*
if ole_1.object.Connect = false then
	MessageBox("OPC connection", "Can not connect to OPC server!! Program will exit!")
	HALT CLOSE
end if
*/
IF SQLca.of_connect( ) = -1 then
	MessageBox("Database", "Unable to connect using " + gs_downtime_ini_file)
	HALT close
ELSE
	string ls_setrole
	ls_setrole = "set role update_role identified by __DB_PASSWORD_REDACTED__"
	EXECUTE IMMEDIATE :ls_setrole USING SQLCA;
	IF sqlca.sqlcode <> 0 then
		MessageBox("Database", "Set role error!!")
	else	
		this.setmicrohelp( "Connection complete")
	end if
end if

//OpenSheet(lw_sheet, "w_da_sheet", this, 0, Original!)
Message.stringparm = ls_window
//of_sendmessage( "pfc_Open")
if gi_screen = 2 then
	if gi_dual_mode = 2 then
		//dual and up down mode
		
		this.x = 0
		this.y = 3353
//		messagebox(string(this.x),string(this.y))
	else
		this.x = 6130
		this.y = 0
	end if
//		messagebox(string(this.x),string(this.y))
end if
postevent( "pfc_open")
return
end event

event pfc_preopen;call super::pfc_preopen;this.of_setsheetmanager( true)
//this.of_setstatusbar( true)
//this.inv_statusbar.of_settimer( true)
//this.inv_statusbar.of_gettimer( ).
end event

