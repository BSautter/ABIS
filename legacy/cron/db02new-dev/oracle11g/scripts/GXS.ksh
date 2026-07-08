#!/usr/bin/ksh

date '+START: %m/%d/%y %H:%M:%S'
RECEIVE="/export/home/oracle11g/edi/receive/"
SEND="/templar/templar/incoming/senddata/"
SFTP_SEND="/templar/templar/incoming/senddata/sftp_send/"
TEMP_FILE="./received_edi_files.txt"
LOGINID="412992496"
UTIL="/templar/templar/util/"
WORK="/export/home/oracle11g/scripts/"
TOVAN_BKUP="ToVan_Bkup"
typeset -i ret_code_in    # Set return code to an integer.
typeset -i ret_code_out   # Set return code to an integer.
typeset -i edi_count      # Set return code to an integer.

ret_code_in=0  # Initialize return code IN.
ret_code_out=0 # Initialize return code IN.

# Process all available outgoing to VAN EDI files
echo " "
echo "Starting outbound processing..."

edi_count=`ls -l /templar/templar/incoming/senddata/S*.edi 2>/dev/null | wc -l`
# echo "edi_count: " $edi_count

if [[ $edi_count -gt 0 ]]; then

    rm $SFTP_SEND*.* # Just in case... This directory should be empty after each run

    cd $SEND # Directory EDI files are supposed to be sent from

    # Send EDI files to VAN one file at a time
    for file in `ls *.edi`
    do
       cp $file $SFTP_SEND # Copy file to sFTP

       cd $WORK # GXS_VAN_OUT.ksh is in this directory

       sftp -b ./GXS_VAN_OUT.ksh $LOGINID@sftp.gateway.inovisworks.net # sFTP file to VAN
       ret_code_out=$?  # Capture outbound sftp return code.

       cd $SFTP_SEND

       # Delete the file from sftp_send directory regardless of sftp return code
       for file_2rm in `ls *.edi`
       do
           rm $file_2rm
       done

       cd $SEND

       if [[ $ret_code_out -eq  0 ]]; then
          mv $file ./ToVan_Bkup/  # Move file to ToVan_Bkup directory
       fi
    done
else
    echo "Nothing to upload to VAN"
fi

#-------------------------------------------------------------------------------------------------------
#-------------------------------------------------------------------------------------------------------
#-------------------------------------------------------------------------------------------------------

# Process all available at the VAN incoming EDI files.
# After processing the electronic documents move them to the backup directory.
echo " "
echo "Starting inbound processing..."

cd $WORK # GXS_VAN_IN.ksh is in this directory

sftp -b ./GXS_VAN_IN.ksh $LOGINID@sftp.gateway.inovisworks.net # sFTP all available files from VAN
ret_code_in=$?  # Capture sftp return code.

if [[ $ret_code_in -eq  0 ]]; then
    # Go to directory where EDI files were downloaded to.
    cd $RECEIVE
    
    edi_count=`ls -l *.edi 2>/dev/null | wc -l`
    
    if [[ $edi_count -gt 0 ]]; then
        [[ -e $TEMP_FILE ]] && rm $TEMP_FILE # Deletes previous list of file names.
    
        for file in `ls *.edi`
        do
           $UTIL/postpro $file
           mv $file ./VanDocuments
           echo "$file" >> $TEMP_FILE
        done
    
        $HOME/scripts/GXS_delete.pl $TEMP_FILE
        $RECEIVE/GXS2.ksh
    else
        echo "No incoming EDI's received."
    fi
else
    echo "Error sFTP files from VAN"
fi

printf "Return code from outbound sftp: [%d}\n" $ret_code_out # Record outbound sFTP return code to log file.
printf "Return code from inbound sftp: [%d}\n" $ret_code_in # Record inbound sFTP return code to log file.
date '+END: %m/%d/%y %H:%M:%S'

exit $ret_code  # Return code of the script is the same as the return code of the sftp.
