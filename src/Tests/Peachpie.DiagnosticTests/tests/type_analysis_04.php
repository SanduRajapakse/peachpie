<?php

function flow_unreachable(/*|mixed|*/$x) {
  /*|integer|*/$y = 42;

  if ($x) {
    $y = null;
    return;
  }

  echo /*|integer|*/$y;
}

function constant_condition_unreachable() {
  /*|integer|*/$y = 42;

  if (false) {
    /*|null|*/$y = null;/*!PHP3012!*/
  }

  // TODO: Test for integer when it's corrected (currently, it's integer|null) 
  echo $y;
}
