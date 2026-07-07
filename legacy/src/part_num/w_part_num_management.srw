$PBExportHeader$w_part_num_management.srw
$PBExportComments$<Sheet> List customer parts and all information about each part, based on customer. Inherited from pfemain\w_sheet
forward
global type w_part_num_management from w_sheet
end type
type dw_order_item_4part from datawindow within w_part_num_management
end type
type cb_routing from commandbutton within w_part_num_management
end type
type cb_obsolete from u_cb within w_part_num_management
end type
type cb_reset from u_cb within w_part_num_management
end type
type cb_search from u_cb within w_part_num_management
end type
type st_cond from u_st within w_part_num_management
end type
type cb_delete from u_cb within w_part_num_management
end type
type st_4 from statictext within w_part_num_management
end type
type st_3 from statictext within w_part_num_management
end type
type st_2 from statictext within w_part_num_management
end type
type dw_pn_list from u_dw within w_part_num_management
end type
type dw_type from u_dw within w_part_num_management
end type
type dw_pn from u_dw within w_part_num_management
end type
type cb_modify from u_cb within w_part_num_management
end type
type rb_showall from u_rb within w_part_num_management
end type
type rb_activeonly from u_rb within w_part_num_management
end type
type cb_report from u_cb within w_part_num_management
end type
type dw_customer from u_dw within w_part_num_management
end type
type cb_close from u_cb within w_part_num_management
end type
type cb_new from u_cb within w_part_num_management
end type
type ddlb_customer from dropdownlistbox within w_part_num_management
end type
type st_1 from statictext within w_part_num_management
end type
type gb_display from u_gb within w_part_num_management
end type
type gb_search from u_gb within w_part_num_management
end type
end forward

global type w_part_num_management from w_sheet
integer x = 5
integer y = 4
integer width = 3790
integer height = 2424
string title = "Customer Part Number management"
string menuname = "m_part_num_management"
boolean maxbox = false
boolean resizable = false
event type string ue_whoami ( )
event ue_read_only ( )
event ue_modify ( )
event ue_new ( )
event ue_delete ( )
event type any ue_get_header ( string as_id )
event ue_set_search ( datawindow adw_d )
event type integer ue_obsolete ( )
event type long ue_create_revision ( long al_old_id )
dw_order_item_4part dw_order_item_4part
cb_routing cb_routing
cb_obsolete cb_obsolete
cb_reset cb_reset
cb_search cb_search
st_cond st_cond
cb_delete cb_delete
st_4 st_4
st_3 st_3
st_2 st_2
dw_pn_list dw_pn_list
dw_type dw_type
dw_pn dw_pn
cb_modify cb_modify
rb_showall rb_showall
rb_activeonly rb_activeonly
cb_report cb_report
dw_customer dw_customer
cb_close cb_close
cb_new cb_new
ddlb_customer ddlb_customer
st_1 st_1
gb_display gb_display
gb_search gb_search
end type
global w_part_num_management w_part_num_management

type variables
Long il_id, il_cust_id
String is_type
Integer ii_status

String is_select
String is_where
s_dw_db  istr_dwdb[]
s_search_condition istr_search[]
Boolean ib_delete
Boolean ib_new
Boolean ib_modify
Long il_cur_part
Boolean ib_readonly
Boolean ib_allcust
Int ii_showall
Long il_index

end variables

forward prototypes
public function string wf_search_terms ()
public subroutine wf_reset_condition ()
public subroutine wf_init_dwdb ()
public function long wf_get_userid (string as_name)
public subroutine wf_update_trimming_data (boolean ab_trimming_required)
public subroutine wf_open_routing ()
public function integer wf_update_routing_4revision_part (long al_routing_sequence, long al_customer_id, long al_part_num_id_old, long al_part_num_id_new)
public function integer wf_check_order_item (long al_part_num_id)
end prototypes

event type string ue_whoami();RETURN "w_part_num_management"
end event

event ue_read_only();//cb_new.Enabled = FALSE
//cb_modify.Enabled = FALSE
end event

event ue_modify();Long ll_used

ll_used = 0
SELECT COUNT(part_num_id) INTO :ll_used
FROM Order_item
WHERE part_num_id = :il_id
USING SQLCA;
IF ll_used > 0 THEN
	MessageBox("Warning", "Can't modify this part because it has already been applied to one or more orders.")
	RETURN
END IF

IF MessageBox("Question", "Modify part information?", Question!, YesNO!, 1 ) = 2 THEN RETURN

gl_message = 0

SetPointer(HourGlass!)

Long ll_rc
s_part_num_info lds_part

lds_part.part_num_id = il_id
lds_part.part_type = is_type

OpenWithParm(w_part_num_modify, lds_part)
ll_rc = Message.DoubleParm

IF ll_rc > 0 THEN
	dw_pn.Retrieve(il_id)
	dw_type.Retrieve(il_id)
END IF



end event

event ue_new();IF MessageBox("Question", "Adding a new part?", Question!, YesNO!, 1 ) = 2 THEN RETURN

gl_message = 0
Long ll_row, ll_i, ll_pos, ll_id

SetPointer(HourGlass!)

s_part_num_info lds_rc
s_part_num_info lds_part

lds_part.part_num_id = il_id
lds_part.part_type = is_type

OpenWithParm(w_part_num_new, lds_part)
lds_rc = Message.PowerObjectParm

IF lds_rc.part_num_id > 0 THEN
	IF dw_customer.retrieve() = -1 THEN
		SQLCA.of_Rollback()
		MessageBox("Error", "w_prod_num_management::ue_new function" )
		RETURN
	End IF
	ddlb_customer.Reset()
	FOR ll_i = 1 TO ll_row
		ddlb_customer.AddItem(dw_customer.GetItemString(ll_i, "customer_short_name"))
	NEXT

	ll_row = dw_customer.RowCount()
	ll_pos = 0
	IF ll_row > 0 THEN
		FOR ll_i = 1 TO ll_row
			ddlb_customer.AddItem(dw_customer.GetItemString(ll_i, "customer_short_name"))
			IF lds_rc.customer_id = dw_customer.GetItemNumber(ll_i, "customer_id") THEN ll_pos = ll_i 
		NEXT
		IF ll_pos > 0 THEN 
			ddlb_customer.SelectItem(0)
			ddlb_customer.SelectItem(ll_pos)
		
			dw_pn_list.Retrieve(lds_rc.customer_id, ii_status)
			ll_row = dw_pn_list.RowCount()
			IF ll_row > 0 THEN
				FOR ll_i = 1 to ll_row
					IF dw_pn_list.GetItemNumber(ll_i, "part_num_id") = lds_rc.part_num_id THEN ll_pos = ll_i
				NEXT
				dw_pn_list.SelectRow(0, FALSE)
				dw_pn_list.SelectRow(ll_pos, TRUE)
		
				dw_pn.Retrieve(lds_rc.part_num_id)
				dw_type.Event ue_show_shape(lds_rc.part_num_id,lds_rc.part_type)
		
				il_id = lds_rc.part_num_id
				is_type = lds_rc.part_type
			ELSE
				SetNULL(il_id)
				SetNULL(is_type)
				dw_type.Reset()
				dw_pn.Reset()
			END IF
		ELSE 
			MessageBox("Error", "Failed to locate new customer.")
			SetNULL(il_id)
			SetNULL(is_type)
			dw_type.Reset()
			dw_pn.Reset()
		END IF
	END IF
END IF

end event

event ue_delete();IF MessageBox("Question", "Delete this part?", Question!, YesNO!, 1 ) = 2 THEN RETURN

gl_message = 0

SetPointer(HourGlass!)

Long ll_used

ll_used = 0
SELECT COUNT(part_num_id) INTO :ll_used
FROM Order_item
WHERE part_num_id = :il_id
USING SQLCA;
IF ll_used > 0 THEN
	MessageBox("Warning", "Can't delete this part because it has already been applied to one or more orders.")
	RETURN
END IF

CHOOSE CASE Upper(Trim(is_type))
	CASE "RECTANGLE"
		DELETE FROM part_num_rectangle
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_rectangle table" )
			RETURN 
		END IF
		
	CASE "PARALLELOGRAM"
		DELETE FROM part_num_parallelogram
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_parallelogram table" )
			RETURN 
		END IF
		
	CASE "FENDER"
		DELETE FROM part_num_fender
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_fender table" )
			RETURN 
		END IF
		
	CASE "CHEVRON"
		DELETE FROM part_num_chevron
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_chevron table" )
			RETURN 
		END IF
		
	CASE "CIRCLE"
		DELETE FROM part_num_circle
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_circle table" )
			RETURN 
		END IF
		
	CASE "TRAPEZOID"
		DELETE FROM part_num_trapezoid
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_trapezoid table" )
			RETURN 
		END IF
		
	CASE "L.TRAPEZOID"
		DELETE FROM part_num_left_trapezoid
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_left_trapezoid table" )
			RETURN 
		END IF
		
	CASE "R.TRAPEZOID"
		DELETE FROM part_num_right_trapezoid
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_right_trapezoid table" )
			RETURN 
		END IF
		
	CASE "REINFORCEMENT"
		DELETE FROM part_num_reinforcement
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_reinforcement table" )
			RETURN 
		END IF
		
	CASE "LIFTGATE"
		DELETE FROM part_num_liftgate
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_liftgate table" )
			RETURN 
		END IF
		
	CASE ELSE
		DELETE FROM part_num_x1_shape
		WHERE part_num_id = :il_id
		USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num_x1_shape table" )
			RETURN 
		END IF
		
END CHOOSE
DELETE FROM part_num
WHERE part_num_id = :il_id
USING SQLCA;
		
		IF SQLCA.SQLCode <> 0 THEN
			ROLLBACK USING SQLCA;
			Messagebox("DBError", "Delete function: part_num table" )
			RETURN 
		END IF
		
COMMIT using SQLCA;

IF il_id > 0 THEN
	dw_pn_list.Event ue_search_retrieve()
	dw_pn_list.SelectRow(0, FALSE)
	dw_pn_list.SelectRow(1, TRUE)
	IF dw_pn_list.RowCount() > 0 THEN
		il_id = dw_pn_list.GetItemNumber(1, "part_num_id")
		is_type = dw_pn_list.GetItemString(1, "sheet_type")
		dw_pn.Retrieve(il_id)
		dw_type.Event ue_show_shape(il_id, is_type)
	ELSE
		dw_pn.Reset()
		dw_type.Reset()
	END IF
END IF
end event

event type any ue_get_header(string as_id);Int li_i
s_dw_header_type lstr_ht

lstr_ht.dw_header = "NULL"
lstr_ht.col_type = "NULL"

li_i = 1
DO WHILE li_i <= UpperBound(istr_dwdb)
	IF istr_dwdb[li_i].dw_column = as_id THEN
		 lstr_ht.dw_header = istr_dwdb[li_i].dw_header
		 lstr_ht.col_type = istr_dwdb[li_i].col_type
	END IF
	li_i++
LOOP

RETURN lstr_ht

end event

event ue_set_search(datawindow adw_d);Long ll_row, ll_l
String ls_s
Int li_i, li_s

ll_row = adw_d.RowCount()
IF ll_row < 1 THEN RETURN
FOR ll_l = 1 TO ll_row
	IF Len(ls_s) > 1 THEN ls_s = ls_s + ', '
	ls_s = ls_s + adw_d.getItemString(ll_l, "column_t", Primary!, FALSE)
	ls_s = ls_s + adw_d.getItemString(ll_l, "operator_t", Primary!, FALSE)
	ls_s = ls_s + adw_d.getItemString(ll_l, "value_t", Primary!, FALSE)
NEXT
st_cond.Text = ls_s

wf_reset_condition()

li_s = 1
FOR ll_l = 1 TO ll_row
	li_i = 1
	DO WHILE li_i <= Upperbound(istr_dwdb)
		IF istr_dwdb[li_i].dw_header = adw_d.getItemString(ll_l, "column_t", Primary!, FALSE) THEN
			istr_search[li_s].db_name = istr_dwdb[li_i].db_name
			istr_search[li_s].db_field = istr_dwdb[li_i].db_field
			istr_search[li_s].col_type = istr_dwdb[li_i].col_type
			istr_search[li_s].operator = adw_d.getItemString(ll_l, "operator_t", Primary!, FALSE)
			istr_search[li_s].value = adw_d.getItemString(ll_l, "value_t", Primary!, FALSE)
			IF istr_search[li_s].col_type = "Customer" THEN
					istr_search[li_s].value = String(wf_get_userid(istr_search[li_s].value))
			END IF
			li_s++
		END IF
	li_i++
	LOOP
NEXT

dw_pn_list.Event ue_search_retrieve()

end event

event type integer ue_obsolete();Integer 	il_cur_status
Integer	li_rtn //Alex Gerlants. 03/11/2025. 2331_Order_Item_4Obsolete_Part

SELECT item_status INTO :il_cur_status
FROM part_num
WHERE part_num_id = :il_id
USING SQLCA;
IF il_cur_status = 0 THEN
	MessageBox("Warning", "Can't obsolete an non-active part.")
	RETURN -1
END IF

IF MessageBox("Question", "Obsolete this part?", Question!, YesNO!, 1 ) = 2 THEN RETURN 0

//Alex Gerlants. 03/11/2025. 2331_Order_Item_4Obsolete_Part. Begin
li_rtn = wf_check_order_item(il_id)

//Do not stop processing ... for now
//If li_rtn = 0 OR li_rtn = -1 Then //Messages are in wf_check_order_item()
//	Return -1
//End If
//Alex Gerlants. 03/11/2025. 2331_Order_Item_4Obsolete_Part. End

UPDATE part_num
SET item_status = 0
WHERE part_num_id = :il_id
USING SQLCA;
		
IF SQLCA.SQLCode <> 0 THEN
	ROLLBACK USING SQLCA;
	Messagebox("DBError", "Obsolete function: part_num table" )
	RETURN -2
END IF

COMMIT using SQLCA;

This.Event ue_create_revision(il_id)

IF il_id > 0 THEN
	dw_pn_list.Event ue_search_retrieve()
	dw_pn_list.SelectRow(0, FALSE)
	dw_pn_list.SelectRow(1, TRUE)
	IF dw_pn_list.RowCount() > 0 THEN
		il_id = dw_pn_list.GetItemNumber(1, "part_num_id")
		is_type = dw_pn_list.GetItemString(1, "sheet_type")
		dw_pn.Retrieve(il_id)
		dw_type.Event ue_show_shape(il_id, is_type)
	ELSE
		dw_pn.Reset()
		dw_type.Reset()
	END IF
END IF
RETURN 1


end event

event type long ue_create_revision(long al_old_id);Integer					li_create_routing, li_count, li_rtn //Alex Gerlants. 12/17/2024. 9999_Work_Center
Long						ll_routing_sequence //Alex Gerlants. 12/17/2024. 9999_Work_Center
str_all_data_types	lstr_all_data_types //Alex Gerlants. 12/17/2024. 9999_Work_Center

IF MessageBox("Question", "Do you want to create a revision of this part?", Question!, YesNO!, 1 ) = 2 THEN RETURN 0

Long ll_new_id
Int li_rc
Long ll_row
Real lr_1, lr_2, lr_3, lr_4, lr_5, lr_6, lr_7, lr_8, lr_9
String ls_shape
Datastore lds_part, lds_shape

lds_part = CREATE datastore  
lds_part.DataObject = "d_new_part_copied"  
lds_part.SetTransObject (SQLCA)  
lds_part.Retrieve(al_old_id)

IF lds_part.RowCount() = 1 THEN
	ls_shape = lds_part.GetItemString(1, "sheet_type")
	lds_part.RowsCopy(1, 1, Primary!, lds_part, 2, Primary!)
	ll_new_id = 0
	ll_new_id = f_get_next_value("part_num_id_seq")
	IF ll_new_id <= 0 THEN
		MessageBox("Error", "Failed to retrieve new id!")
		DESTROY lds_part
		RETURN -3
	END IF
	lds_part.SetItem(2,"part_num_id", ll_new_id)
	lds_part.SetItem(2,"item_status", 1)
	lds_part.AcceptText()
	li_rc = lds_part.Update()
	IF (li_rc = 1) OR IsNULL(li_rc) THEN
		//COMMIT USING SQLCA;
	ELSE
		MessageBox("Error", "Failed to save part item changes!")
		ROLLBACK USING SQLCA;
		DESTROY lds_part
		RETURN -1
	END IF

	lds_shape = CREATE datastore  
	CHOOSE CASE Upper(Trim(ls_shape))
		CASE "RECTANGLE"
			lds_shape.DataObject = "d_pn_rectangle_new"
		CASE "PARALLELOGRAM"
			lds_shape.DataObject = "d_pn_parallelogram_new"
		CASE "FENDER"
			lds_shape.DataObject = "d_pn_fender_new"
		CASE "CHEVRON"
			lds_shape.DataObject = "d_pn_chevron_new"
		CASE "CIRCLE"
			lds_shape.DataObject = "d_pn_circle_new"
		CASE "TRAPEZOID"
			lds_shape.DataObject = "d_pn_trapezoid_new"
		CASE "L.TRAPEZOID"
			lds_shape.DataObject = "d_pn_ltrapezoid_new"
		CASE "R.TRAPEZOID"
			lds_shape.DataObject = "d_pn_rtrapezoid_new"
		CASE "REINFORCEMENT"
			lds_shape.DataObject = "d_pn_reinforcement_new"
		CASE "LIFTGATE"
			lds_shape.DataObject = "d_pn_liftgate_shape_new"	
		CASE ELSE
			lds_shape.DataObject = "d_pn_x1shape_new"
	END CHOOSE
	lds_shape.SetTransObject (SQLCA)  
	lds_shape.Retrieve(al_old_id)	
	lds_shape.RowsCopy(1, 1, Primary!, lds_shape, 2, Primary!)
	lds_shape.SetItem(2,"part_num_id", ll_new_id)
	
	li_rc = lds_shape.Update()
	IF (li_rc = 1) OR IsNULL(li_rc) THEN
		
		//Alex Gerlants. 12/17/2024. 9999_Work_Center. Begin
		li_create_routing = MessageBox("Question", "Would you like to use the old part ID routing for the new part ID?", Question!, YesNo!, 1)
		
		If li_create_routing = 1 Then //Yes
			lstr_all_data_types.long_var[1] = il_cust_id
			lstr_all_data_types.long_var[2] = al_old_id
			lstr_all_data_types.long_var[3] = ll_new_id
			
			select	count(*)
			into		:li_count
			from     routing
						join part_num on part_num.part_num_id = routing.part_num_id
						join customer on customer.customer_id = routing.customer_id
						join line on line.line_num = routing.line_num
						join die on die.die_id = routing.die_id
			where    routing.part_num_id = :al_old_id
			and      routing.customer_id = :il_cust_id
			using		sqlca;
			
			If sqlca.sqlcode < 0 Then //DB error
				MessageBox("Database error", "Database error in ue_create_revision for w_part_num_management while counting routings." + &
																"~nPlease contact abis support" + "~n~rError:~n" + &
																sqlca.sqlerrtext)
			Else //OK
				If li_count > 0 Then
					If li_count = 1 Then
						select	routing_sequence
						into		:ll_routing_sequence
						from		routing
						where    routing.part_num_id = :al_old_id
						and      routing.customer_id = :il_cust_id
						using		sqlca;
						
						If sqlca.sqlcode < 0 Then //DB error
							MessageBox("Database error", "Database error in ue_create_revision for w_part_num_management while retrieving routing sequence." + &
																			"~nPlease contact abis support" + "~n~rError:~n" + &
																			sqlca.sqlerrtext)
							Return -3
						End If
						
						li_rtn = wf_update_routing_4revision_part(ll_routing_sequence, il_cust_id, al_old_id, ll_new_id)
						
						If li_rtn = -1 Then
							Return -3
						End If
					Else //li_count > 1
						OpenWithParm(w_routing_4customer_and_part, lstr_all_data_types) //Let user select a routing to update part_num_id to ll_new_id
					End If
				End If //If li_count > 0 Then
			End If
		Else //No
			
		End If
		//Alex Gerlants. 12/17/2024. 9999_Work_Center. End__DB_PASSWORD_REDACTED__
		
		COMMIT USING SQLCA;
	ELSE
		MessageBox("Error", "Failed to save part type changes!")
		ROLLBACK USING SQLCA;
		DESTROY lds_part
		DESTROY lds_shape
		RETURN -2
	END IF
END IF

DESTROY lds_part
DESTROY lds_shape

MessageBox("New part", "A new part ID " + String(ll_new_id) + " has been created successfully." )

RETURN ll_new_id
	
	


end event

public function string wf_search_terms ();String ls_terms
integer li_i

ls_terms = is_where
li_i = 1
DO WHILE li_i <= UpperBound(istr_search)
	IF istr_search[li_i].db_name <> "" THEN
		CHOOSE CASE istr_search[li_i].col_type
			CASE "Number"
				IF Len(ls_terms) < 2 THEN 
					ls_terms = " WHERE " + " ( ~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~" " + istr_search[li_i].operator + " " +istr_search[li_i].value + " )"
				ELSE
					ls_terms = ls_terms + " AND ( ~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~" " + istr_search[li_i].operator + " " +istr_search[li_i].value + " )"
				END IF
			CASE "Customer"
				IF Len(ls_terms) < 2 THEN 
					ls_terms = " WHERE ( ~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~" " + istr_search[li_i].operator + " " +istr_search[li_i].value + " )"
				ELSE
					ls_terms = ls_terms + " AND ( ~~~"" + istr_search[li_i].db_name + "~~~".~~~"" + istr_search[li_i].db_field + "~~~" " + istr_search[li_i].operator + " " +istr_search[li_i].value + " )"
				END IF
			CASE "Status"
				IF Len(ls_terms) < 2 THEN 
					ls_terms = " WHERE ( ~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~" " + istr_search[li_i].operator + istr_search[li_i].value + " )"
				ELSE
					ls_terms = ls_terms + " AND ( ~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~" " + istr_search[li_i].operator + istr_search[li_i].value + " )"
				END IF
			CASE "Date"
				IF Len(ls_terms) < 2 THEN 
					ls_terms =" WHERE (TO_DATE(TO_CHAR(~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~",~~~'DD-MON-YYYY~~~'),~~~'DD-MON-YYYY~~~'  ) " + istr_search[li_i].operator + " TO_DATE(~~~'" + istr_search[li_i].value + "~~~',~~~'mm/dd/yyyy~~~' )" + " )"  //21-DEC-1998 format
				ELSE
					ls_terms = ls_terms + " AND (TO_DATE(TO_CHAR(~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~",~~~'DD-MON-YYYY~~~'),~~~'DD-MON-YYYY~~~'  ) " + istr_search[li_i].operator + " TO_DATE(~~~'" + istr_search[li_i].value + "~~~',~~~'mm/dd/yyyy~~~' )" + " )"  //21-DEC-1998 format
				END IF
			CASE "String"
				IF Len(ls_terms) < 2 THEN 
					ls_terms = " WHERE ( ~"" + istr_search[li_i].db_name + "~".~"" + istr_search[li_i].db_field + "~" " + istr_search[li_i].operator + " ~~~'" + istr_search[li_i].value + "~~~' )"
				ELSE
					ls_terms = ls_terms + " AND ( ~~~"" + istr_search[li_i].db_name + "~~~".~~~"" + istr_search[li_i].db_field + "~~~" " + istr_search[li_i].operator + " ~~~'" + istr_search[li_i].value + "~~~' )"
				END IF
		END CHOOSE
	END IF
	li_i++
LOOP

Return ls_terms
end function

public subroutine wf_reset_condition ();Int li_i
li_i = 1
DO WHILE li_i <= Upperbound(istr_search)
	istr_search[li_i].db_name = ""
	istr_search[li_i].db_field = ""
	istr_search[li_i].operator = ""
	istr_search[li_i].value = ""
	istr_search[li_i].col_type = ""
	li_i++
LOOP
is_where = "  WHERE ( ~~~"PART_NUM~~~".~~~"CUSTOMER_ID~~~" = :al_cust and  ~~~"PART_NUM~~~".~~~"ITEM_STATUS~~~"  >= :ai_status )"		

end subroutine

public subroutine wf_init_dwdb ();istr_dwdb[1].db_name = "PART_NUM"
istr_dwdb[1].db_field = "SHEET_TYPE"
istr_dwdb[1].dw_column = "sheet_type"
istr_dwdb[1].col_type = "String"
istr_dwdb[1].dw_header = "Type"

istr_dwdb[2].db_name = "PART_NUM"
istr_dwdb[2].db_field = "item_desc"
istr_dwdb[2].dw_column = "item_desc"
istr_dwdb[2].col_type = "String"
istr_dwdb[2].dw_header = "Item Description"

istr_dwdb[3].db_name = "PART_NUM"
istr_dwdb[3].db_field = "ALLOY"
istr_dwdb[3].dw_column = "alloy"
istr_dwdb[3].col_type = "String"
istr_dwdb[3].dw_header = "Alloy"

istr_dwdb[4].db_name = "PART_NUM"
istr_dwdb[4].db_field = "TEMPER"
istr_dwdb[4].dw_column = "temper"
istr_dwdb[4].col_type = "String"
istr_dwdb[4].dw_header = "Temper"

istr_dwdb[5].db_name = "PART_NUM"
istr_dwdb[5].db_field = "ENDUSER_ID"
istr_dwdb[5].dw_column = "enduser_id"
istr_dwdb[5].col_type = "Customer"
istr_dwdb[5].dw_header = "End User"


end subroutine

public function long wf_get_userid (string as_name);Long ll_id, ll_row, ll_i
DataStore lds_u

ll_id = 0

lds_u = CREATE DataStore
lds_u.DataObject = "d_dddw_id_short"
lds_u.SetTransObject(SQLCA)
lds_u.Retrieve()

ll_row = lds_u.RowCount()
IF ll_row > 0 THEN
	FOR ll_i = 1 TO ll_row
		IF Upper(Trim(lds_u.GetItemString(ll_i, "customer_short_name"))) = Upper(Trim(as_name)) THEN
			ll_id = lds_u.GetItemNumber(ll_i, "customer_id")
		END IF
	NEXT
END IF

DESTROY lds_u

RETURN ll_id
end function

public subroutine wf_update_trimming_data (boolean ab_trimming_required);/*
Function:	wf_update_trimming_data
				Update to NULL value 4 part_num columns: 
					trimming_required, trim_type_code, incoming_coil_width, and trimmed_coil_width
Returns:		none
Arguments:	value	boolean	ab_trimming_required
*/

Decimal{3}	ld_null

SetNull(ld_null)

If Not ab_trimming_required Then
	dw_pn.Object.trimming_required[dw_pn.GetRow()] = "N"
	dw_pn.Object.trim_type_code[dw_pn.GetRow()] = ld_null
	dw_pn.Object.incoming_coil_width[dw_pn.GetRow()] = ld_null
	dw_pn.Object.trimmed_coil_width[dw_pn.GetRow()] = ld_null
End If
end subroutine

public subroutine wf_open_routing ();//Alex Gerlants. 11/15/2024. 9999_Work_Center. Begin
/*
Function:	wf_open_routing
Returns:		none
Arguments:	none
*/

SetPointer(HourGlass!)

Window  					lw_Sheet, lw_OpenSheets[]   
Integer 					li_rc, li_i, li_rtn
Long						ll_row, ll_part_num_id
w_table_routing		lw_table_routing
str_all_data_types	lstr_all_data_types

ll_row = dw_pn.GetRow()

If ll_row > 0 Then
	ll_part_num_id = dw_pn.Object.part_num_id[1]
	
	lstr_all_data_types.long_var[1] = il_cust_id
	lstr_all_data_types.long_var[2] = ll_part_num_id
	lstr_all_data_types.string_var[1] = "FROM PART_NUM"
	
	//MessageBox("wf_open_routing for w_part_num_management", "il_cust_id = " + String(il_cust_id) + "~nll_part_num_id = " + String(ll_part_num_id))
	
	If Not f_this_window_open("Table Routing") Then
		SetPointer(HourGlass!)
		li_rtn = OpenSheetWithParm(lw_table_routing, lstr_all_data_types, gnv_app.of_getframe(), 0, Original!)
	End If
	
	ArrangeSheets(Cascade!)
End If
end subroutine

public function integer wf_update_routing_4revision_part (long al_routing_sequence, long al_customer_id, long al_part_num_id_old, long al_part_num_id_new);//Alex Gerlants. 12/17/2024. 9999_Work_Center. Begin
/*
Function:	wf_update_routing_4revision_part
Returns:		integer
Arguments:	value	long	al_routing_sequence
				value	long	al_customer_id
				value	long	al_part_num_id_old
				value	long	al_part_num_id_new
*/
Integer	li_rtn = 1
String	ls_customer_short_name

update	routing
set		part_num_id = :al_part_num_id_new
where		routing_sequence = :al_routing_sequence
and		customer_id = :al_customer_id
and      part_num_id = :al_part_num_id_old
using		sqlca;

//Commit is in ue_create_revision where this function is called from

If sqlca.sqlcode < 0 Then //DB error
	li_rtn = -1

	select	customer_short_name
	into		:ls_customer_short_name
	from 		customer
	where		customer_id = :al_customer_id
	using 	sqlca;

	MessageBox("Database error", "Database error in wf_update_routing_4revision_part for w_part_num_management while updating table routing for:" + &
													"~n~rRouting Sequence " + String(al_routing_sequence) + &
													"~n~rCustomer " + ls_customer_short_name + &
													"~n~rPart ID " + String(al_part_num_id_old) + &
													"~nPlease contact abis support" + "~n~rError:~n" + sqlca.sqlerrtext)
End If

Return li_rtn
//Alex Gerlants. 12/17/2024. 9999_Work_Center. End
end function

public function integer wf_check_order_item (long al_part_num_id);//Alex Gerlants. 03/11/2025. 2331_Order_Item_4Obsolete_Part. Begin
/*
Finction:	wf_check_order_item
Arguments:	value	ling	al_part_num_id
Returns:		integer	 1 if OK to obsolete a part
							 0 if not OK to obsolete a part
							-1 if DB error
*/

Integer	li_rtn, li_rows, li_row
Long		ll_part_num_id
String	ls_customer_short_name, ls_item_desc
String	ls_order_items, ls_order_abc_num, ls_order_item_num, ls_item_status_desc, ls_part

li_rows = dw_order_item_4part.Retrieve(al_part_num_id)

If sqlca.sqlcode <> 0 Then //DB error
	li_rtn = -1
	MessageBox("DB error", "Database error in wf_check_order_item for w_part_num_management.~nError~nsqlca.SqlErrText", StopSign!)
	Return li_rtn
End If

If li_rows > 0 Then
	li_rtn = 0 // Not OK to obsolete a part
	
	ll_part_num_id = dw_order_item_4part.Object.part_num_id[1]
	ls_item_desc = dw_order_item_4part.Object.item_desc[1]
	ls_customer_short_name = dw_order_item_4part.Object.customer_short_name[1]
	
	ls_part = 	"~nCustomer: " + ls_customer_short_name + "~n" + &
					"Part Number: " + String(ll_part_num_id) + "~n" + &
					"Part Description: " + ls_item_desc + "~n~n"
	
	ls_order_items = "Order Items:~n----------------~n"
	
	For li_row = 1 To li_rows
		ls_order_abc_num = String(dw_order_item_4part.Object.order_abc_num[li_row])
		ls_order_item_num = String(dw_order_item_4part.Object.order_item_num[li_row])
		ls_item_status_desc = dw_order_item_4part.Object.item_status_desc[li_row]
		
		ls_order_items = ls_order_items + "Order #: " + ls_order_abc_num + ". Item #: " + ls_order_item_num + ". Order Item Status: " + ls_item_status_desc + "~n"
	Next
	
	If li_rows = 1 Then
		MessageBox("Order item in a wrong status exist", 	"You are trying to obsolete a part linked to an active order item " + &
																			"in status other then 'Done' or 'Cancelled'." + "~n" + &
																			"Please update order item status to 'Done' or 'Cancelled'." + "~n" + &
																			ls_part + ls_order_items)
	Else //li_rows > 1
		MessageBox("Order items in a wrong status exist", 	"You are trying to obsolete a part linked to " + String(li_rows) + " active order items " + &
																			"in status other then 'Done' or 'Cancelled'." + "~n" + &
																			"Please update order item status to 'Done' or 'Cancelled'." + "~n" + &
																			ls_part + ls_order_items)
	End If
Else //li_rows <= 0
	li_rtn = 1 //OK to obsolete a part
End If

Return li_rtn
//Alex Gerlants. 03/11/2025. 2331_Order_Item_4Obsolete_Part. End
end function

on w_part_num_management.create
int iCurrent
call super::create
if this.MenuName = "m_part_num_management" then this.MenuID = create m_part_num_management
this.dw_order_item_4part=create dw_order_item_4part
this.cb_routing=create cb_routing
this.cb_obsolete=create cb_obsolete
this.cb_reset=create cb_reset
this.cb_search=create cb_search
this.st_cond=create st_cond
this.cb_delete=create cb_delete
this.st_4=create st_4
this.st_3=create st_3
this.st_2=create st_2
this.dw_pn_list=create dw_pn_list
this.dw_type=create dw_type
this.dw_pn=create dw_pn
this.cb_modify=create cb_modify
this.rb_showall=create rb_showall
this.rb_activeonly=create rb_activeonly
this.cb_report=create cb_report
this.dw_customer=create dw_customer
this.cb_close=create cb_close
this.cb_new=create cb_new
this.ddlb_customer=create ddlb_customer
this.st_1=create st_1
this.gb_display=create gb_display
this.gb_search=create gb_search
iCurrent=UpperBound(this.Control)
this.Control[iCurrent+1]=this.dw_order_item_4part
this.Control[iCurrent+2]=this.cb_routing
this.Control[iCurrent+3]=this.cb_obsolete
this.Control[iCurrent+4]=this.cb_reset
this.Control[iCurrent+5]=this.cb_search
this.Control[iCurrent+6]=this.st_cond
this.Control[iCurrent+7]=this.cb_delete
this.Control[iCurrent+8]=this.st_4
this.Control[iCurrent+9]=this.st_3
this.Control[iCurrent+10]=this.st_2
this.Control[iCurrent+11]=this.dw_pn_list
this.Control[iCurrent+12]=this.dw_type
this.Control[iCurrent+13]=this.dw_pn
this.Control[iCurrent+14]=this.cb_modify
this.Control[iCurrent+15]=this.rb_showall
this.Control[iCurrent+16]=this.rb_activeonly
this.Control[iCurrent+17]=this.cb_report
this.Control[iCurrent+18]=this.dw_customer
this.Control[iCurrent+19]=this.cb_close
this.Control[iCurrent+20]=this.cb_new
this.Control[iCurrent+21]=this.ddlb_customer
this.Control[iCurrent+22]=this.st_1
this.Control[iCurrent+23]=this.gb_display
this.Control[iCurrent+24]=this.gb_search
end on

on w_part_num_management.destroy
call super::destroy
if IsValid(MenuID) then destroy(MenuID)
destroy(this.dw_order_item_4part)
destroy(this.cb_routing)
destroy(this.cb_obsolete)
destroy(this.cb_reset)
destroy(this.cb_search)
destroy(this.st_cond)
destroy(this.cb_delete)
destroy(this.st_4)
destroy(this.st_3)
destroy(this.st_2)
destroy(this.dw_pn_list)
destroy(this.dw_type)
destroy(this.dw_pn)
destroy(this.cb_modify)
destroy(this.rb_showall)
destroy(this.rb_activeonly)
destroy(this.cb_report)
destroy(this.dw_customer)
destroy(this.cb_close)
destroy(this.cb_new)
destroy(this.ddlb_customer)
destroy(this.st_1)
destroy(this.gb_display)
destroy(this.gb_search)
end on

event open;call super::open;ii_status = 1  //active only

dw_customer.Visible = FALSE
dw_customer.SetTransObject(sqlca) 
IF dw_customer.retrieve() = -1 THEN
	SQLCA.of_Rollback()
	MessageBox("Error", "w_prod_num_management::open function" )
ELSE
	dw_customer.SelectRow(1,TRUE)
	dw_customer.SetFocus()
End IF

Long ll_row, ll_i, ll_id
ll_row = dw_customer.RowCount()
IF ll_row > 0 THEN
	ll_id = dw_customer.GetItemNumber(1, "customer_id")
	il_cust_id = ll_id
	
	FOR ll_i = 1 TO ll_row
		ddlb_customer.AddItem(dw_customer.GetItemString(ll_i, "customer_short_name"))
	NEXT
	ddlb_customer.SelectItem(1)
	
	dw_pn_list.Retrieve(ll_id, ii_status)
	IF dw_pn_list.RowCount() > 0 THEN 
		dw_pn_list.SelectRow(0, FALSE)
		dw_pn_list.SelectRow(1, TRUE)
		il_id = dw_pn_list.GetItemNumber(1, "part_num_id")
		is_type = dw_pn_list.GetItemString(1, "sheet_type")
		dw_pn.Retrieve(il_id)
		dw_type.Event ue_show_shape(il_id, is_type)
	END IF

END IF

wf_init_dwdb()
is_select = "SELECT  ~~~"PART_NUM~~~".~~~"PART_NUM_ID~~~" , ~~~"PART_NUM~~~".~~~"SHEET_TYPE~~~" ,~~~"PART_NUM~~~".~~~"CUSTOMER_ID~~~" ,~~~"PART_NUM~~~".~~~"ITEM_DESC~~~" ,~~~"PART_NUM~~~".~~~"ITEM_STATUS~~~" ,~~~"PART_NUM~~~".~~~"ALLOY~~~" ,           ~~~"PART_NUM~~~".~~~"TEMPER~~~" ,~~~"PART_NUM~~~".~~~"ENDUSER_ID~~~"  FROM ~~~"PART_NUM~~~"    "  
is_where = "  WHERE ( ~~~"PART_NUM~~~".~~~"CUSTOMER_ID~~~" = :al_cust and  ~~~"PART_NUM~~~".~~~"ITEM_STATUS~~~"  >= :ai_status )"		

dw_order_item_4part.setTransObject(sqlca) //Alex Gerlants. 03/11/2025. 2331_Order_Item_4Obsolete_Part
//dw_order_item_4part.Visible = False
end event

event pfc_print;IF MessageBox("Question", "Print out current part's information?", Question!, OKCancel!, 1) = 1 THEN
	OpenWithParm(w_report_part_num, il_id)
END IF

RETURN 0

end event

event pfc_new;IF MessageBox("Question", "Create a new part?", Question!, YesNO!, 1 ) = 2 THEN RETURN

gl_message = 0

SetPointer(HourGlass!)

//OpenWithParm(w_part_num_new, lds_coil)
//ids_coil = Message.PowerObjectParm
//
//IF ids_coil.customer_id > 0 THEN
//	dw_editor.Event pfc_addRow()
//	wf_editing_mode()
//END IF
//


end event

event close;call super::close;f_display_app()
end event

event activate;call super::activate;IF f_security_door("Part Number") = 1 THEN RETURN 1
This.Event ue_read_only()
end event

type dw_order_item_4part from datawindow within w_part_num_management
integer x = 3950
integer y = 1024
integer width = 1280
integer height = 544
integer taborder = 160
string title = "none"
string dataobject = "d_order_item_4part"
boolean livescroll = true
borderstyle borderstyle = stylelowered!
end type

type cb_routing from commandbutton within w_part_num_management
integer x = 2523
integer y = 2136
integer width = 338
integer height = 92
integer taborder = 150
integer textsize = -8
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
string text = "Routing"
end type

event clicked;//Alex Gerlants. 9999_Work_Center
//If Not f_this_window_open("Routing") Then
//	SetPointer(HourGlass!)
//	Message.StringParm = "w_table_routing"
//	gnv_app.of_getFrame().Event pfc_open()
//End If

wf_open_routing() //Alex Gerlants. 11/15/2024. 9999_Work_Center

end event

type cb_obsolete from u_cb within w_part_num_management
string tag = "obsolete a part"
integer x = 1253
integer y = 2136
integer width = 338
integer taborder = 120
fontcharset fontcharset = ansi!
string facename = "Arial"
string text = "&Obsolete"
end type

event clicked;call super::clicked;RETURN Parent.Event ue_obsolete()
end event

type cb_reset from u_cb within w_part_num_management
integer x = 1234
integer y = 1840
integer width = 297
integer taborder = 60
string facename = "Arial"
string text = "&Reset"
end type

event clicked;call super::clicked;dw_pn_list.Event ue_resume_where()
end event

type cb_search from u_cb within w_part_num_management
integer x = 1234
integer y = 1740
integer width = 297
integer taborder = 40
string facename = "Arial"
string text = "&Search"
end type

event clicked;call super::clicked;OpenWithParm(w_buildfilter, dw_pn_list.DataObject)
Return 1
end event

type st_cond from u_st within w_part_num_management
integer x = 73
integer y = 1760
integer width = 1083
integer height = 168
string facename = "Arial"
string text = "All"
end type

type cb_delete from u_cb within w_part_num_management
integer x = 1682
integer y = 2136
integer width = 338
integer taborder = 110
fontcharset fontcharset = ansi!
string facename = "Arial"
string text = "&Delete"
end type

event clicked;call super::clicked;Parent.Event ue_delete()
RETURN 1
end event

type st_4 from statictext within w_part_num_management
integer x = 869
integer y = 1392
integer width = 229
integer height = 52
integer textsize = -8
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 16777215
long backcolor = 67108864
string text = "Obsoleted"
boolean focusrectangle = false
end type

type st_3 from statictext within w_part_num_management
integer x = 699
integer y = 1488
integer width = 155
integer height = 52
integer textsize = -8
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 67108864
string text = "Active"
boolean focusrectangle = false
end type

type st_2 from statictext within w_part_num_management
integer x = 338
integer y = 1488
integer width = 352
integer height = 52
integer textsize = -8
integer weight = 400
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 67108864
string text = "Color Indication:"
boolean focusrectangle = false
end type

type dw_pn_list from u_dw within w_part_num_management
event type long ue_show_only ( )
event ue_resume_where ( )
event type integer ue_search_retrieve ( )
integer y = 156
integer width = 1563
integer height = 1320
integer taborder = 30
boolean titlebar = true
string title = "Please select a part from the list:"
string dataobject = "d_pn_list_per_customer"
end type

event type long ue_show_only();Long ll_row, ll_i
ll_row = dw_customer.RowCount()
IF ll_row > 0 THEN
	il_id = dw_customer.GetItemNumber(1, "customer_id")
	
	ddlb_customer.SelectItem(1)

	dw_pn_list.Retrieve(il_id, ii_status)
	dw_pn_list.SelectRow(0, FALSE)
	dw_pn_list.SelectRow(1, TRUE)
	IF dw_pn_list.RowCount() > 0 THEN 
		il_id = dw_pn_list.GetItemNumber(1, "part_num_id")
		is_type = dw_pn_list.GetItemString(1, "sheet_type")
	END IF
	dw_pn.Retrieve(il_id)
	dw_type.Event ue_show_shape(il_id, is_type)
ELSE
	RETURN 0
END IF

RETURN 1

end event

event ue_resume_where();String ls_modify, ls_where, ls_rc

wf_reset_condition()
ls_modify ="DataWindow.Table.Select = '" + is_select + is_where + " '"
ls_rc = this.Modify(ls_modify)
IF ls_rc = "" THEN
	this.Retrieve(il_cust_id,ii_status)
ELSE
	MessageBox("Error","Failure to resume datawindow!", StopSign!)
END IF
st_cond.Text = "All"
end event

event type integer ue_search_retrieve();String ls_modify, ls_where, ls_rc
Long ll_cust, ll_row
integer li_net

IF this.Event pfc_UpdatesPending() = 1 THEN
	li_net = MessageBox("Search...", "Initial searching,save changes first ?", Question!, OKCancel!,1)
	if li_net = 1 then
		Parent.Event pfc_Save()      
	else
	  	return 0
	end if
END IF

ls_where = wf_search_terms()

//ll_row = dw_customer.GetRow()
//ll_cust = dw_customer.GetItemNumber(ll_row, "customer_id")
ls_modify ="DataWindow.Table.Select = '" + is_select + ls_where + " '"
ls_rc = this.Modify(ls_modify)
IF ls_rc = "" THEN
	this.Retrieve(il_cust_id,ii_status)
ELSE
	MessageBox("Error","Failure to modify datawindow: " + ls_rc + ": " + ls_where, StopSign!)
END IF

Return 1
end event

event constructor;call super::constructor;of_SetBase(TRUE)
of_SetRowSelect(TRUE)
of_SetRowManager(TRUE)
of_SetSort(TRUE)
inv_sort.of_SetColumnHeader(TRUE)
inv_sort.of_SetUseDisplay(TRUE)
inv_RowSelect.of_SetStyle ( 0 ) 
of_SetFilter(TRUE)
SetTransObject(SQLCA)



end event

event pfc_rowchanged;call super::pfc_rowchanged;Integer 	li_return
long 		li_Row
Long		ll_id //Alex Gerlants. 03/13/2017
String	ls_type //Alex Gerlants. 03/13/2017

this.AcceptText()	
li_Row = this.GetRow()

If li_row <= 0 Then Return  //Alex Gerlants. 10/14/2024. 9999_Work_Center. Not related to Work Center. Discovered by Dan Polkinhorne

this.SelectRow(0, False)
this.SelectRow(li_Row, True)

this.ScrollToRow(li_Row)

//Alex Gerlants. 03/13/2017. Begin
ls_type = This.GetItemString(li_Row, "sheet_type")
is_type = ls_type

If IsNull(ls_type) Then
	MessageBox("Error", "Invalid sheet type")
	Return 
End If

ll_id = This.GetItemNumber(li_Row, "part_num_id")
il_id = ll_id

If ll_id <= 0 Then 
	MessageBox("Error", "Invalid part id!")
	Return 
End If

dw_pn.Retrieve(ll_id)
dw_type.Event ue_show_shape(ll_id, ls_type)
//Alex Gerlants. 03/13/2017. End

Return 

end event

event rowfocuschanged;call super::rowfocuschanged;this.Event pfc_rowchanged()
end event

event rbuttondown;RETURN 0
end event

event rbuttonup;RETURN 0
end event

event clicked;//Override pfc_clicked

integer li_rc, li_status

// Check arguments
IF IsNull(xpos) or IsNull(ypos) or IsNull(row) or IsNull(dwo) THEN
	Return
END IF

IF IsValid (inv_RowSelect) THEN
	inv_RowSelect.Event pfc_clicked ( xpos, ypos, row, dwo )
END IF

IF IsValid (inv_Sort) THEN 
	inv_Sort.Event pfc_clicked ( xpos, ypos, row, dwo ) 
END IF 

//my code


IF row <= 0 THEN return

this.selectrow(0, FALSE)
this.selectrow(row, TRUE)

il_id = this.GetItemNumber(row, "part_num_id")
IF il_id <= 0 THEN 
	MessageBox("Error", "Invalid part id!")
	Return 
END IF

is_type = this.GetItemString(row, "sheet_type")
IF IsNULL(is_type) THEN 
	MessageBox("Error", "Invalid sheet type")
	Return 
END IF

SetPointer(HourGlass!)

dw_pn.Retrieve(il_id)
dw_type.Event ue_show_shape(il_id,is_type)

RETURN

end event

event pfc_retrievedddw;call super::pfc_retrievedddw;DataWindowChild dddw_cni

IF this.GetChild(as_column, dddw_cni) = -1 THEN
	Return -1
ELSE
	dddw_cni.SetTransObject(SQLCA)
	
	Return dddw_cni.Retrieve() 
END IF
end event

event doubleclicked;call super::doubleclicked;cb_modify.Event Clicked() //Alex Gerlants. 01/08/2018
end event

type dw_type from u_dw within w_part_num_management
event type long ue_show_shape ( long al_id,  string as_shape )
integer x = 1573
integer y = 1500
integer width = 2203
integer height = 620
integer taborder = 70
string dataobject = "d_pn_ltrapezoid_display"
boolean vscrollbar = false
boolean livescroll = false
end type

event type long ue_show_shape(long al_id, string as_shape);CHOOSE CASE Upper(Trim(as_shape))
	CASE "RECTANGLE"
		this.DataObject = "d_pn_rectangle_display"
	CASE "PARALLELOGRAM"
		this.DataObject = "d_pn_parallelogram_display"
	CASE "FENDER"
		this.DataObject = "d_pn_fender_display"
	CASE "CHEVRON"
		this.DataObject = "d_pn_chevron_display"
	CASE "CIRCLE"
		this.DataObject = "d_pn_circle_display"
	CASE "TRAPEZOID"
		this.DataObject = "d_pn_trapezoid_display"
	CASE "L.TRAPEZOID"
		this.DataObject = "d_pn_ltrapezoid_display"
	CASE "R.TRAPEZOID"
		this.DataObject = "d_pn_rtrapezoid_display"
	CASE "REINFORCEMENT"
		this.DataObject = "d_pn_reinforcement_display"
	CASE "LIFTGATE"
		this.DataObject = "d_pn_liftgate_shape_display"	
	CASE ELSE
		this.DataObject = "d_pn_x1shape_display"
END CHOOSE
this.SetTransObject(SQLCA)
this.Retrieve(al_id)

RETURN 1
end event

type dw_pn from u_dw within w_part_num_management
integer x = 1550
integer width = 2226
integer height = 1484
integer taborder = 50
string dataobject = "d_pn_item_detail_display"
boolean vscrollbar = false
boolean livescroll = false
end type

event constructor;call super::constructor;SetTransObject(SQLCA)

end event

event pfc_retrievedddw;call super::pfc_retrievedddw;DataWindowChild dddw_cni

IF this.GetChild(as_column, dddw_cni) = -1 THEN
	Return -1
ELSE
	dddw_cni.SetTransObject(SQLCA)
	
	Return dddw_cni.Retrieve() 
END IF
end event

event rbuttondown;return 1
end event

event rbuttonup;return 1
end event

event retrieveend;call super::retrieveend;String	ls_item_note, ls_modstring //Alex Gerlants. 03/13/2017

//Alex Gerlants. 03/13/2017. Begin
If rowcount > 0 Then
	ls_item_note = This.Object.item_note[This.GetRow()]
	f_display_num_of_chars(This, ls_item_note, "num_of_char_t")
End If
//Alex Gerlants. 03/13/2017. End
end event

type cb_modify from u_cb within w_part_num_management
integer x = 818
integer y = 2136
integer width = 338
integer taborder = 100
fontcharset fontcharset = ansi!
string facename = "Arial"
string text = "&Modify"
end type

event clicked;call super::clicked;Parent.Event ue_modify()
RETURN 1
end event

type rb_showall from u_rb within w_part_num_management
integer x = 869
integer y = 2020
integer width = 315
integer height = 68
string facename = "Arial"
string text = "Show All"
end type

event clicked;call super::clicked;ii_status = 0 //show all

RETURN dw_pn_list.Event ue_show_only()

end event

type rb_activeonly from u_rb within w_part_num_management
integer x = 265
integer y = 2020
integer width = 320
integer height = 68
string facename = "Arial"
string text = "Active Only"
boolean checked = true
end type

event clicked;call super::clicked;ii_status = 1 //active only

RETURN dw_pn_list.Event ue_show_only()
end event

type cb_report from u_cb within w_part_num_management
integer x = 2117
integer y = 2136
integer width = 338
integer taborder = 130
fontcharset fontcharset = ansi!
string facename = "Arial"
string text = "&Print"
end type

event clicked;SetPointer(HourGlass!)
Parent.Event pfc_print()
end event

type dw_customer from u_dw within w_part_num_management
integer x = 1175
integer y = 8
integer width = 110
integer height = 44
integer taborder = 0
string dataobject = "d_pn_customer_short_display"
boolean vscrollbar = false
end type

event pfc_retrieve;call super::pfc_retrieve;Return this.Retrieve()
end event

event clicked;SelectRow(0, FALSE)

// Check arguments
IF IsNull(xpos) or IsNull(ypos) or IsNull(row) or IsNull(dwo) THEN
	Return
END IF

// Notify the RowSelect service that the row has been clicked.
IF IsValid (inv_RowSelect) THEN
	inv_RowSelect.Event pfc_clicked ( xpos, ypos, row, dwo )
END IF
SelectRow(row, TRUE)

// Sort services.
IF IsValid (inv_Sort) THEN 
	// Notify the Sort service, since it may have have a request
	// to sort on colum header.
	inv_Sort.Event pfc_clicked ( xpos, ypos, row, dwo ) 
END IF 











end event

event constructor;SetRowFocusIndicator(Hand!) 
of_SetRowSelect(TRUE)
of_SetRowManager(TRUE)
of_SetSort(TRUE)
inv_RowSelect.of_SetStyle ( 3 ) 


end event

event rbuttondown;//disbaled
Return 0
end event

event rbuttonup;//disbaled
Return 0
end event

event pfc_rowchanged;call super::pfc_rowchanged;Integer li_return
long li_Row

this.AcceptText()
li_Row = this.GetRow()
this.SelectRow(0, False)
this.SelectRow(li_Row, True)
this.ScrollToRow(li_Row)



Return 

end event

event rowfocuschanged;call super::rowfocuschanged;this.Event pfc_rowchanged()
end event

type cb_close from u_cb within w_part_num_management
string tag = "Exit"
integer x = 3387
integer y = 2136
integer width = 338
integer taborder = 140
string facename = "Arial"
string text = "&Close"
end type

event clicked;call super::clicked;Close(Parent)
end event

type cb_new from u_cb within w_part_num_management
string tag = "Create a new part"
integer x = 389
integer y = 2136
integer width = 338
integer taborder = 90
boolean bringtotop = true
fontcharset fontcharset = ansi!
string facename = "Arial"
string text = "&New"
end type

event clicked;parent.Event ue_new()
RETURN 1
end event

type ddlb_customer from dropdownlistbox within w_part_num_management
integer x = 50
integer y = 64
integer width = 1440
integer height = 996
integer taborder = 10
boolean bringtotop = true
integer textsize = -8
integer weight = 700
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
boolean sorted = false
boolean vscrollbar = true
integer limit = 20
borderstyle borderstyle = stylelowered!
end type

event selectionchanged;String ls_text
Long ll_row, ll_i

ls_text = Text(index)
ll_row = dw_customer.RowCount()
FOR ll_i = 1 TO ll_row
	IF dw_customer.GetItemString(ll_i, "customer_short_name") = ls_text THEN
		il_cust_id = dw_customer.GetItemNumber(ll_i, "customer_id")
		//is_where = "  WHERE ( ~~~"PART_NUM~~~".~~~"CUSTOMER_ID~~~" = :al_cust and  ~~~"PART_NUM~~~".~~~"ITEM_STATUS~~~"  >= :ai_status )"		
		dw_pn_list.Retrieve(il_cust_id, ii_status)
		dw_pn_list.SelectRow(0, FALSE)
		dw_pn_list.SelectRow(1, TRUE)
		IF dw_pn_list.RowCount() > 0 THEN
			il_id = dw_pn_list.GetItemNumber(1, "part_num_id")
			is_type = dw_pn_list.GetItemString(1, "sheet_type")
			dw_pn.Retrieve(il_id)
			dw_type.Event ue_show_shape(il_id, is_type)
		ELSE
			dw_pn.Reset()
			dw_type.Reset()
		END IF
	END IF
NEXT




end event

type st_1 from statictext within w_part_num_management
integer x = 50
integer width = 425
integer height = 60
boolean bringtotop = true
integer textsize = -8
integer weight = 700
fontcharset fontcharset = ansi!
fontpitch fontpitch = variable!
fontfamily fontfamily = swiss!
string facename = "Arial"
long textcolor = 33554432
long backcolor = 79741120
boolean enabled = false
string text = "Current customer:"
boolean focusrectangle = false
end type

type gb_display from u_gb within w_part_num_management
integer x = 9
integer y = 1960
integer width = 1536
integer height = 156
integer taborder = 80
integer weight = 700
string facename = "Arial"
string text = "Display"
end type

type gb_search from u_gb within w_part_num_management
integer x = 9
integer y = 1688
integer width = 1536
integer height = 272
integer taborder = 20
integer weight = 700
string facename = "Arial"
string text = "Search"
end type

