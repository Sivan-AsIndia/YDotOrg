import { ComponentFixture, TestBed } from '@angular/core/testing';

import { ForgotpasswordComponent } from './forgotpassword';

describe('Forgotpassword', () => {
  let component: ForgotpasswordComponent;
  let fixture: ComponentFixture<ForgotpasswordComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ForgotpasswordComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ForgotpasswordComponent);
    component = fixture.componentInstance;
    await fixture.whenStable();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });
});
