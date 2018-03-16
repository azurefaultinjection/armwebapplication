$(document).ready(function () {
  /*"westcentalus": "westus",
    "germanycental": "uksouth",
    "germanynortheast": "uksouth",
    "ukwest": "uksouth",
    "westindia": "centralindia",
    "korealcentral": "japanwest",
      "koreasouth": "japanwest"
  */
  var regions = [
    { id: "eastus2", name: "East US 2" },
    { id: "centralus", name: "Central US" },
    { id: "westeurope", name: "West Europe" },
    { id: "francecentral", name: "France Central" }
  ];
  $('#example-getting-started').multiselect();
  $('form').on('submit', onSubmit);
  $('form').on('click',
    function (e) {
      $('multiselect-native-select ul').css('display', 'none');
    });
});

$('#myWizard').easyWizard({
  buttonsClass: 'btn',
  submitButtonClass: 'btn btn-info',
  before: function (wizardObj, currentStepObj, nextStepObj) {
    if (!currentStepObj || currentStepObj[0].getAttribute("id") !== 'authenticate') {
      return;
    }
    if (isValid(currentStepObj)) {
      getSubscriptions(currentStepObj);
    }
  },
  after: function (wizardObj, prevStepObj, currentStepObj) {
    //alert('Hello, I\'am the after callback');
  },
  beforeSubmit: function (wizardObj) {
    // alert('Hello, I\'am the beforeSubmit callback');
  }
});

function isValid(currentStepObj) {


  var isValid = true;
  currentStepObj.find('input').each(function () {
    if ($.trim($(this).val()) == '') {
      isValid = false;
      $(this).css({
        "border": "1px solid red",
        "background": "#FFCECE"
      });
    }
    else {
      $(this).css({
        "border": "",
        "background": ""
      });
    }
  });

  return isValid;
  //if (isValid == false)
  //  e.preventDefault();
  // }
}

function getResourceGroups(subscription) {
  var tenantId = $.find("#inputTenantId")[0].value;
  var clientId = $.find("#inputClientId")[0].value;
  var clientSecret = $.find("#inputClientSecret")[0].value;
  var request = $.ajax({
    url: "api/FaultInjection/getresourcegroups",
    type: "GET",
    data: { tenantId: tenantId, clientId: clientId, clientSecret: clientSecret, subscription: subscription }
  });
  request.done(function (result) {
    if (!result) {
      console.log("resource group list is empty");
      return;
    }
    console.log("InItlize Multi list");

    bindOptions($('#excludedResourceGroups'), result);
    bindOptions($('#includedResourceGroups'), result);
    initializeMultipleSelection("excludedResourceGroups");
    initializeMultipleSelection("includedResourceGroups");
  });

  request.fail(function (jqXHR, textStatus) {
    alert("Request failed: " + textStatus);
  });
}

function bindOptions($element, result) {
  $element.empty();
  $.each(result, function (index, item) {
    $element.append(
      $('<option/>', {
        value: item.id,
        text: item.displayName ? item.displayName : item.name
      })
    );
  });
}

$("#selectSubscription").change(function () {
  var subscription = this.value;
  getResourceGroups(subscription);
});

function getSubscriptions(currentStepObj) {
  var tenantId = currentStepObj.find("#inputTenantId").val();
  var clientId = currentStepObj.find("#inputClientId").val();
  var clientSecret = currentStepObj.find("#inputClientSecret").val();
  var request = $.ajax({
    url: "api/FaultInjection/getsubscriptions",
    type: "GET",
    data: { tenantId: tenantId, clientId: clientId, clientSecret: clientSecret }
  });
  request.done(function (result) {
    if (!result) {
      console.log("subscription list is empty");
      return;
    }

    bindOptions($('#selectSubscription'), result);
    getResourceGroups(result[0].id);
  });

  request.fail(function (jqXHR, textStatus) {
    alert("Request failed: " + textStatus);
  });
}

var rGrps = {
  excludedResourceGroups: {},
  includedResourceGroups: {}
};

function initializeMultipleSelection(id) {
  $('#' + id).multiselect({
    buttonContainer: '<div id="' + id + '-container"></div>',
    onChange: function (option, checked, select) {
      var val = $(option).val();
      rGrps[id][val] = checked;
      //// Get checkbox corresponding to option:
      //var value = $(options).val();
      //var $input = $('#' + id + '-container input[value="' + value + '"]');

      //// Adapt label class:
      //if (selected) {
      //  $input.closest('label').addClass('active');
      //} else {
      //  $input.closest('label').removeClass('active');
      //}
    }
  });

  $('#' + id + '-container').find('button').on('click',
    function (e) {
      var display = $(e.target).next('ul').css('display');
      if (display === 'block')
        $(e.target).next('ul').css('display', 'none');
      else
        $(e.target).next('ul').css('display', 'block');
    });
}

function getSubsId(id) {
  return id.split('/')[2];
}

function onSubmit(e) {
  e.preventDefault();
  var values = {};
  $.each($(e.target).serializeArray(), function (i, field) {
    if (field.value === 'on') {
      field.value = true;
    }
    if (field.value === 'off') {
      field.value = false;
    }
    if (field.name === 'subscription') {
      values[field.name] = getSubsId(field.value);
    }
    else if (field.name === 'includedResourceGroups') {
      values[field.name] = Object.keys(rGrps.includedResourceGroups)
        .filter(grp => rGrps.includedResourceGroups[grp]);
    }
    else if (field.name === 'excludedResourceGroups') {
      values[field.name] = Object.keys(rGrps.excludedResourceGroups)
        .filter(grp => rGrps.excludedResourceGroups[grp]);
    } else {
      values[field.name] = field.value;
    }
  });
  var request = $.ajax({
    url: "api/FaultInjection/createblob",
    type: "POST",
    data: values
  });
  request.done(function (result) {
    if (!result) {
      console.log("subscription list is empty");
      return;
    }
  });

  request.fail(function (jqXHR, textStatus) {
    alert("Request failed: " + textStatus);
  });
}